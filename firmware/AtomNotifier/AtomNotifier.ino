// M5Stack ATOM Matrix (ESP32-PICO-D4). Library: Adafruit NeoPixel.
#include <Arduino.h>
#include <Adafruit_NeoPixel.h>
#include <BluetoothSerial.h>
#include <esp_sleep.h>
#include <esp_bt.h>
#include <driver/rtc_io.h>

#if !defined(CONFIG_BT_SPP_ENABLED)
#error This firmware requires the original ESP32 with Bluetooth Classic SPP.
#endif
BluetoothSerial SerialBT;

Adafruit_NeoPixel leds(25, 27, NEO_GRB + NEO_KHZ800);
const uint8_t BUTTON_PIN = 39;
RTC_DATA_ATTR bool pending[3] = {false, false, false};
RTC_DATA_ATTR int shown = -1;
RTC_DATA_ATTR uint32_t wakeCount = 0;
constexpr uint32_t SLEEP_SECONDS = 20;
constexpr uint32_t RECEIVE_WINDOW_MS = 10000;
constexpr uint32_t DISPLAY_MS = 4000;
bool modemSleepEnabled = false;
bool sleepRequested = false;
bool receivedNotification = false;
uint32_t sleepDeadline = 0, displayUntil = 0;
bool pixelsAreOff = false;
bool previewWake = false;
const char* names[] = {"CHAT", "CODEX", "DOTS"};
const uint8_t icons[3][5] = {
  {0b11111, 0b10001, 0b10101, 0b11111, 0b01000},
  {0b01010, 0b10001, 0b10001, 0b10001, 0b01010},
  {0b00000, 0b01010, 0b00000, 0b00100, 0b00000}
};
char input[64];
uint8_t inputLength = 0;
bool overflow = false;
uint32_t lastHeartbeat = 0, lastFrame = 0, lastCycle = 0;
bool rawButton = false, stableButton = false, longHandled = false;
uint32_t buttonChange = 0, buttonDown = 0;

int nextPending(int after) {
  for (int n = 1; n <= 3; ++n) {
    int i = (after + n + 3) % 3;
    if (pending[i]) return i;
  }
  return -1;
}

void previewPending(uint32_t now) {
  if (shown < 0 || !pending[shown]) shown = nextPending(-1);
  lastCycle = now;
  displayUntil = shown >= 0 ? now + DISPLAY_MS : 0;
  sleepDeadline = now + RECEIVE_WINDOW_MS;
}

void command(const char* line) {
  if (strcmp(line, "PING") == 0) {
    lastHeartbeat = millis();
    SerialBT.println("PONG ATOM_NOTIFIER_BT_LP_V4");
    return;
  }
  if (strcmp(line, "STATUS") == 0) {
    SerialBT.printf("CPU%u MODEM%u WAKE%lu PENDING%u\n", getCpuFrequencyMhz(),
      modemSleepEnabled, (unsigned long)wakeCount,
      (unsigned)(pending[0] + pending[1] + pending[2]));
    return;
  }
  if (strncmp(line, "CLEAR ", 6) == 0 && line[6] >= '0' && line[6] <= '7' && line[7] == 0) {
    uint8_t mask = line[6] - '0';
    for (int i = 0; i < 3; ++i) if (mask & (1 << i)) pending[i] = false;
    if (shown >= 0 && !pending[shown]) shown = nextPending(shown);
    if (shown < 0) displayUntil = 0;
    SerialBT.println("OK");
    return;
  }
  if (strcmp(line, "SLEEP") == 0) {
    SerialBT.printf("SLEEPING %lu\n", (unsigned long)SLEEP_SECONDS);
    sleepRequested = true;
    uint32_t now = millis();
    uint32_t remaining = (int32_t)(displayUntil - now) > 0 ? displayUntil - now : 0;
    sleepDeadline = now + (remaining > 200 ? remaining : 200);
    return;
  }
  for (int i = 0; i < 3; ++i) {
    char expected[16];
    snprintf(expected, sizeof(expected), "N %s", names[i]);
    if (strcmp(line, expected) == 0) {
      pending[i] = true;
      shown = i;
      lastCycle = millis();
      lastHeartbeat = millis();
      receivedNotification = true;
      displayUntil = millis() + DISPLAY_MS;
      sleepDeadline = displayUntil + 1500;
      SerialBT.println("OK");
      return;
    }
  }
  SerialBT.println("ERR");
}

void readSerial() {
  while (SerialBT.available()) {
    char c = SerialBT.read();
    if (c == '\r') continue;
    if (c == '\n') {
      if (!overflow) { input[inputLength] = 0; command(input); }
      else SerialBT.println("ERR TOO_LONG");
      inputLength = 0;
      overflow = false;
    } else if (!overflow) {
      if (inputLength < sizeof(input) - 1) input[inputLength++] = c;
      else overflow = true;
    }
  }
}

void readButton(uint32_t now) {
  bool pressed = digitalRead(BUTTON_PIN) == LOW;
  if (pressed != rawButton) { rawButton = pressed; buttonChange = now; }
  if ((uint32_t)(now - buttonChange) >= 30 && rawButton != stableButton) {
    stableButton = rawButton;
    if (stableButton) {
      buttonDown = now;
      longHandled = false;
      // A dark panel means preview, even during an awake Bluetooth window.
      previewWake = shown < 0 || (int32_t)(displayUntil - now) <= 0;
      if (previewWake) previewPending(now);
    }
    else if (!longHandled && shown >= 0) {
      if (previewWake) {
        previewWake = false;
        displayUntil = now + DISPLAY_MS;
        // Leave the receive window open for the PC to synchronize held items.
        return;
      }
      // Confirmation remains local; no unsolicited messages on the command link.
      pending[shown] = false;
      shown = nextPending(shown);
      lastCycle = now;
    }
  }
  if (stableButton && !longHandled && (uint32_t)(now - buttonDown) >= 1500) {
    for (int i = 0; i < 3; ++i) pending[i] = false;
    shown = -1;
    longHandled = true;
  }
}

void paint(uint32_t now) {
  if ((uint32_t)(now - lastFrame) < 40) return;
  lastFrame = now;
  if (shown < 0) shown = nextPending(-1);
  if (!stableButton && shown >= 0 && (uint32_t)(now - lastCycle) >= 1000) {
    shown = nextPending(shown);
    lastCycle = now;
  }
  leds.clear();
  if (shown >= 0) {
    // Gentle pulse; total brightness is capped at 8/255.
    uint16_t phase = now / 10 % 300;
    uint8_t level = 80 + (phase < 150 ? phase : 300 - phase);
    uint32_t color = shown == 0 ? leds.Color(0, level, level / 3) :
                     shown == 1 ? leds.Color(0, level / 3, level) :
                                  leds.Color(level, 0, level);
    for (int y = 0; y < 5; ++y)
      for (int x = 0; x < 5; ++x)
        if (icons[shown][y] & (1 << (4 - x))) leds.setPixelColor(y * 5 + x, color);
  }
  // No connection/status pixels. Only unread notifications may light up.
  if (shown < 0 || (int32_t)(displayUntil - now) <= 0) {
    if (!pixelsAreOff) { leds.clear(); leds.show(); pixelsAreOff = true; }
    return;
  }
  pixelsAreOff = false;
  leds.show();
}

void enterDeepSleep() {
  // GPIO39 has an external pull-up on the board. Never sleep while held low.
  if (digitalRead(BUTTON_PIN) == LOW || stableButton) return;
  leds.clear(); leds.show();
  SerialBT.flush();
  delay(60);
  SerialBT.end(); // Stop controller/host before sleeping; RTC keeps unread flags.
  rtc_gpio_init(GPIO_NUM_39);
  rtc_gpio_set_direction(GPIO_NUM_39, RTC_GPIO_MODE_INPUT_ONLY);
  esp_sleep_enable_ext0_wakeup(GPIO_NUM_39, 0);
  esp_sleep_enable_timer_wakeup((uint64_t)SLEEP_SECONDS * 1000000ULL);
  Serial.flush();
  esp_deep_sleep_start();
}

void setup() {
  Serial.begin(115200);
  setCpuFrequencyMhz(80);
  ++wakeCount;
  rtc_gpio_deinit(GPIO_NUM_39);
  pinMode(BUTTON_PIN, INPUT); // GPIO39 has no internal pull-up; board has one.
  leds.begin();
  leds.setBrightness(8);
  leds.clear();
  leds.show();
  SerialBT.enableSSP(false, false);
  if (esp_sleep_get_wakeup_cause() == ESP_SLEEP_WAKEUP_EXT0) {
    // The wake press previews retained items; a subsequent press acknowledges.
    rawButton = stableButton = true;
    previewWake = true;
    buttonDown = millis();
    previewPending(buttonDown);
    // Show RTC-retained notifications before synchronous Bluetooth startup.
    lastFrame = buttonDown - 40;
    paint(buttonDown);
  }
  if (!SerialBT.begin("ATOM-Notify", false, true)) {
    Serial.println("Bluetooth startup failed");
  } else {
    modemSleepEnabled = esp_bt_sleep_enable() == ESP_OK;
    Serial.printf("READY ATOM_NOTIFIER_BT_LP_V4 CPU%u MODEM%u WAKE%lu\n",
      getCpuFrequencyMhz(), modemSleepEnabled, (unsigned long)wakeCount);
  }
  sleepDeadline = millis() + RECEIVE_WINDOW_MS;
  if (previewWake) previewPending(millis());
}

void loop() {
  uint32_t now = millis();
  static bool wasConnected = false;
  bool connected = SerialBT.hasClient();
  if (connected != wasConnected) {
    inputLength = 0; overflow = false;
    lastHeartbeat = 0;
    wasConnected = connected;
  }
  readSerial();
  readButton(now);
  paint(now);
  if ((int32_t)(now - sleepDeadline) >= 0) enterDeepSleep();
  // Block the application task between updates rather than polling at 500 Hz.
  delay(shown >= 0 && (int32_t)(displayUntil - now) > 0 ? 40 : 100);
}

# FLUXA — MR Trading Chart for Meta Quest 3

A mixed-reality crypto trading workspace built for the **Meta Quest 3** and **Logitech MX Ink** stylus. Your chart lives in your physical space — persistent, always visible, always live.

![Platform](https://img.shields.io/badge/Platform-Meta%20Quest%203-blue)
![Unity](https://img.shields.io/badge/Unity-6000.3.11f1-black)
![Status](https://img.shields.io/badge/Status-Alpha-orange)

---

## What It Does

- Live crypto candlestick chart (BTC, ETH, SOL, BNB, XRP, DOGE) via Bybit WebSocket
- Floating panel in MR passthrough — stays in your room while you work
- Full stylus interaction via MX Ink — click, draw, switch pairs, toggle indicators
- TradingView-style drawing tools: trendlines, horizontal lines, Fibonacci, rectangles, freehand

---

## Requirements

| Item | Details |
|---|---|
| Headset | Meta Quest 3 |
| Stylus | Logitech MX Ink (paired via Bluetooth) |
| Sideloading | SideQuest or ADB |
| Internet | Required for live chart data |

---

## Install Guide

### Step 1 — Enable Developer Mode on Quest 3

1. Install the **Meta app** on your phone if you haven't already
2. Open the Meta app → tap your **headset name**
3. Go to **Settings** → **Developer Mode** → toggle it **ON**
4. Put your headset on — it may ask you to confirm, press **Allow**

> Developer Mode must be enabled or the APK install will be blocked.

---

### Step 2 — Install SideQuest on your PC

SideQuest is the easiest way to sideload apps.

1. Download **SideQuest (Advanced Installer)** from [sidequestvr.com](https://sidequestvr.com/setup-howto)
2. Install and open SideQuest
3. Connect your Quest 3 to your PC via **USB-C cable**
4. Put on the headset — it will ask **"Allow USB debugging?"** → tap **Allow**
5. SideQuest should show a **green dot** at the top left (connected)

---

### Step 3 — Download the APK

Download the latest release from:

**[github.com/Gary029/fluxa-mr-trading/releases](https://github.com/Gary029/fluxa-mr-trading/releases)**

Click **Fluxa-v0.1.apk** to download it to your PC.

---

### Step 4 — Install via SideQuest

1. In SideQuest, click the **box with an arrow icon** (Install APK) in the top bar
2. Select the downloaded `Fluxa-v0.1.apk` file
3. Wait for **"APK installed successfully"** message

---

### Step 5 — Launch FLUXA

1. Put on your Quest 3
2. Go to your **App Library**
3. Filter by **Unknown Sources** (top right dropdown)
4. Find **FLUXA** and tap to launch
5. The chart panel will appear floating in front of you in MR passthrough

---

### Alternative: Install via ADB (advanced)

If you prefer command line:

```bash
# Install ADB (Android Platform Tools)
# https://developer.android.com/tools/releases/platform-tools

adb devices                        # confirm Quest is connected
adb install "Fluxa-v0.1.apk"      # install the APK
```

---

### Troubleshooting Install

| Problem | Fix |
|---|---|
| SideQuest shows red dot | Check USB cable, re-allow USB debugging in headset |
| "Allow USB debugging?" never appeared | Try a different USB cable (data cable, not charge-only) |
| App not visible in library | Filter by Unknown Sources |
| Chart is black on launch | Wait 5–10 seconds, check internet connection |
| MX Ink cursor not moving | Pair MX Ink in Quest Bluetooth settings before launching |

---

## Testing Guide

This section walks you through testing every feature end-to-end after sideloading.

### Before You Start
- Quest 3 is on, passthrough is enabled
- MX Ink is paired via Bluetooth (hold the front button until the LED blinks, pair in Quest Bluetooth settings)
- You are connected to the internet (chart needs live data)
- Launch FLUXA from **Unknown Sources** in your app library

---

### 1. Chart Loads
| What to check | Expected |
|---|---|
| App opens | Floating white panel appears in front of you |
| Chart renders | BTC/USDT candlestick chart with live candles |
| Panel position | Roughly arm's length away at eye level |

> If the chart is black or blank — wait 5 seconds, the WebView is loading. If still blank, check your internet connection.

---

### 2. Cursor / Pointer
| What to do | Expected |
|---|---|
| Hold MX Ink and move it around | Crosshair cursor moves across the chart |
| Move tip close to the panel surface | Cursor snaps and registers as a click |
| Move tip far away | Cursor stops, no click |

---

### 3. Tip Click (Toolbar Buttons)
Point at any button in the chart toolbar (BTC, ETH, timeframe buttons etc.) and bring your tip close to the panel.

| Button | Expected |
|---|---|
| BTC / ETH / SOL / BNB / XRP / DOGE | Chart switches to that pair |
| 1m / 5m / 15m / 1H / 4H / 1D / 1W | Timeframe changes |

---

### 4. Front Button Gestures
The **front button** is the larger button on the side of the MX Ink facing you.

| Gesture | How to do it | Expected |
|---|---|---|
| Single tap | Quick press and release | Cycles indicator: EMA20 → EMA50 → BB → VWAP → RSI → MACD → Stoch → PDH/L |
| Double tap | Two quick presses | Cycles drawing tool |
| Triple tap | Three quick presses | Switches trading pair: BTC → ETH → SOL → BNB → XRP → DOGE |
| Hold + move | Hold for 0.2s then move tip | Freehand line drawn on chart |

> Tap speed matters — keep taps under ~0.4 seconds apart for double/triple to register.

---

### 5. Back Button Gestures
The **back button** is the smaller button on the opposite side.

| Gesture | Expected |
|---|---|
| Single tap | Undo last drawing |
| Double tap | Cycle timeframe: 1m → 5m → 15m → 1h → 4h → 1D → 1W |

---

### 6. Drawing Tools
1. Double tap front button to select a tool (watch the toolbar highlight change)
2. Bring tip close to chart to place first point
3. Move and tap again for second point
4. Double tap front button again to switch to next tool or deselect

---

### 7. Known Limitations (Alpha)
- Chart requires active internet — no offline mode
- Panel spawns at a fixed position on launch — walk around if it spawns off-screen
- MX Ink must be paired before launching the app
- Only supports Meta Quest 3 (not Quest 2 or Quest Pro)

---

### Reporting Issues
Found a bug? [Open an issue](https://github.com/Gary029/fluxa-mr-trading/issues) and include:
- What you did
- What you expected
- What happened instead
- Your MX Ink firmware version (found in Logitech Options+ app)

---

## MX Ink Gesture Map

| Gesture | Action |
|---|---|
| Move stylus near panel | Cursor follows tip position |
| Tip touch panel (proximity) | Click — place chart tool point, press buttons |
| Front button single tap | Cycle indicator (EMA20 → EMA50 → BB → VWAP → RSI → MACD → Stoch → PDH/L) |
| Front button double tap | Cycle drawing tool (Trend → H-Line → Fib → Rect → Pen → Position) |
| Front button triple tap | Cycle trading pair (BTC → ETH → SOL → BNB → XRP → DOGE) |
| Front button hold + move | Freehand draw on chart |
| Back button single tap | Undo last drawing |
| Back button double tap | Cycle timeframe (1m → 5m → 15m → 1h → 4h → 1D → 1W) |

---

## Building from Source

### Prerequisites
- Unity **6000.3.11f1**
- Android Build Support module (installed via Unity Hub)
- Meta XR SDK (included via Package Manager)
- [SimpleUnity3DWebView](https://assetstore.unity.com/packages/tools/gui/3d-webview-for-android-with-gecko-engine-web-browser-158778) *(paid asset — purchase and import into `Assets/Plugins/`)*

### Steps
1. Clone the repo
2. Purchase and import SimpleUnity3DWebView into `Assets/Plugins/`
3. Open project in Unity 6000.3.11f1
4. Switch platform to Android (File → Build Settings → Android → Switch Platform)
5. Build and Run to connected Quest 3

---

## Architecture

```
Assets/
  Scripts/
    Input/
      StylusInputManager.cs     — MX Ink pose tracking, gesture detection (tap/hold)
      MxInkWebViewBridge.cs     — Stylus → WebView JS injection bridge
    WebView/
      FluxaWebViewManager.cs    — SimpleUnity3DWebView wrapper
    FLUXAPanel.cs               — MR panel positioning and visibility
  StreamingAssets/
    WebView/
      chart.html                — TradingView Lightweight Charts + Bybit WebSocket
```

---

## Roadmap

- [ ] Portfolio panel (P&L, open positions)
- [ ] Order entry via stylus
- [ ] Multi-panel layout
- [ ] Hand tracking fallback (no stylus)
- [ ] Alerts and price notifications

---

## License

MIT — free to use, modify, and distribute.

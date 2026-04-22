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

## Install (Sideload)

1. Enable **Developer Mode** on your Quest 3 (Meta mobile app → Headset Settings → Developer Mode)
2. Download the latest `.apk` from [Releases](https://github.com/Gary029/fluxa-mr-trading/releases)
3. Install via **SideQuest** (drag and drop the APK) or ADB:
   ```
   adb install "Fluxa-v0.1.apk"
   ```
4. Launch from **Unknown Sources** in your Quest app library

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

// The phones the harness looks through, by Playwright's own descriptors: viewport, pixel ratio, touch
// and user agent. An iPhone is drawn by WebKit, Safari's engine, and a Galaxy by Chromium, which is
// what each one's browser is built on.
//
// The default set spans the widths that break layouts: the narrowest iPhone still sold, the common
// iPhone, the widest, the common Android width and a wide Android.

export const DEVICES = {
  "iphone-se": { descriptor: "iPhone SE (3rd gen)", engine: "webkit" },
  "iphone-17": { descriptor: "iPhone 17", engine: "webkit" },
  "iphone-17-pro-max": { descriptor: "iPhone 17 Pro Max", engine: "webkit" },
  "galaxy-s24": { descriptor: "Galaxy S24", engine: "chromium" },
  "galaxy-a55": { descriptor: "Galaxy A55", engine: "chromium" },
  "galaxy-z-flip-7": { descriptor: "Galaxy Z Flip 7", engine: "chromium" },
  "galaxy-s9-plus": { descriptor: "Galaxy S9+", engine: "chromium" },
  "pixel-9": { descriptor: "Pixel 9", engine: "chromium" },
};

export const DEFAULT_DEVICES = ["iphone-se", "iphone-17", "iphone-17-pro-max", "galaxy-s24", "galaxy-a55"];

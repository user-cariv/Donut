(() => {
  const icon = document.getElementById("ios-home-icon");
  if (!icon) return;
  const darkMode = window.matchMedia("(prefers-color-scheme: dark)");
  const androidManifest = document.createElement("link");
  androidManifest.rel = "manifest";
  androidManifest.id = "android-home-manifest";
  document.head.append(androidManifest);

  const syncIconToDeviceTheme = () => {
    const appearance = darkMode.matches ? "dark" : "light";
    icon.href = `donut-home-${appearance}.png?v=4`;
    androidManifest.href = `site-${appearance}.webmanifest?v=4`;
  };

  syncIconToDeviceTheme();
  darkMode.addEventListener?.("change", syncIconToDeviceTheme);
})();

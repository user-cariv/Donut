(() => {
  const themeKey = "donut-theme";
  const media = window.matchMedia("(prefers-color-scheme: dark)");

  const savedTheme = () => {
    try {
      const saved = localStorage.getItem(themeKey);
      if (saved === "dark" || saved === "light") return saved;

      // Carry forward the old Donut Island preference into the shared setting.
      const islandSaved = localStorage.getItem("di_theme");
      if (islandSaved === "dark" || islandSaved === "light") {
        localStorage.setItem(themeKey, islandSaved);
        return islandSaved;
      }
    } catch (_) {}
    return null;
  };

  const applyTheme = (theme) => {
    const dark = theme === "dark";
    document.documentElement.setAttribute("data-theme", dark ? "dark" : "light");
    document.body.classList.toggle("dark-mode", dark);
    const themeColor = document.querySelector('meta[name="theme-color"]');
    if (themeColor) {
      themeColor.content = themeColor.getAttribute(dark ? "data-theme-dark" : "data-theme-light") || (dark ? "#080808" : "#ffffff");
    }
    window.dispatchEvent(new Event("donut-theme-change"));
  };

  const currentSavedTheme = savedTheme;
  const syncFromPreference = () => applyTheme(currentSavedTheme() || (media.matches ? "dark" : "light"));
  syncFromPreference();

  document.addEventListener("click", (event) => {
    if (!event.target.closest(".hub-theme-toggle, .theme-toggle, #theme")) return;
    const nextTheme = document.body.classList.contains("dark-mode") ? "light" : "dark";
    applyTheme(nextTheme);
    try { localStorage.setItem(themeKey, nextTheme); } catch (_) {}
  });

  window.addEventListener("storage", (event) => {
    if (event.key === themeKey || event.key === null) syncFromPreference();
  });

  const handleSystemThemeChange = () => {
    if (!currentSavedTheme()) syncFromPreference();
  };
  if (media.addEventListener) media.addEventListener("change", handleSystemThemeChange);
  else media.addListener?.(handleSystemThemeChange);
})();

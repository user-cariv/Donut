const themeToggle = document.querySelector(".hub-theme-toggle");
let storedTheme = null;
try { storedTheme = localStorage.getItem("donut-theme"); } catch (_) {}
if (storedTheme === "dark") document.body.classList.add("dark-mode");

if (themeToggle) {
  themeToggle.addEventListener("click", () => {
    document.body.classList.toggle("dark-mode");
    try { localStorage.setItem("donut-theme", document.body.classList.contains("dark-mode") ? "dark" : "light"); } catch (_) {}
  });
}

const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
if (!reduceMotion) {
  const heroArt = document.querySelector(".hub-hero-art");
  if (heroArt) {
    window.addEventListener("pointermove", (event) => {
      const x = (event.clientX / window.innerWidth - 0.5) * 8;
      const y = (event.clientY / window.innerHeight - 0.5) * 8;
      heroArt.style.transform = `translate(${x}px, ${y}px)`;
    }, { passive: true });
  }
}

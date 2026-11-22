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

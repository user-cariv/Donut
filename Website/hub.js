const heroArt = document.querySelector(".hub-hero-art");

if (heroArt) {
  for (const pill of heroArt.querySelectorAll(".hub-float")) {
    const surface = pill.firstElementChild;
    let drag;

    function place(x, y) {
      const maxX = Math.max(0, heroArt.clientWidth - pill.offsetWidth);
      const maxY = Math.max(0, heroArt.clientHeight - pill.offsetHeight);
      const left = Math.max(0, Math.min(maxX, x));
      const top = Math.max(0, Math.min(maxY, y));
      pill.style.inset = `${top}px auto auto ${left}px`;
    }

    surface.addEventListener("pointerdown", (event) => {
      if (event.button !== 0 || drag) return;
      surface.setPointerCapture(event.pointerId);
      const stage = heroArt.getBoundingClientRect();
      const bounds = surface.getBoundingClientRect();
      place(bounds.left - stage.left, bounds.top - stage.top);
      pill.classList.add("is-dragged", "is-dragging");
      drag = { id: event.pointerId, x: event.clientX - bounds.left, y: event.clientY - bounds.top };
    });

    surface.addEventListener("pointermove", (event) => {
      if (drag?.id !== event.pointerId) return;
      const stage = heroArt.getBoundingClientRect();
      place(event.clientX - stage.left - drag.x, event.clientY - stage.top - drag.y);
    });

    surface.addEventListener("lostpointercapture", () => {
      drag = undefined;
      pill.classList.remove("is-dragging");
    });

    new ResizeObserver(() => {
      if (pill.classList.contains("is-dragged")) place(pill.offsetLeft, pill.offsetTop);
    }).observe(heroArt);
  }
}

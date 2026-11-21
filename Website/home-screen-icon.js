(() => {
  const scheme = window.matchMedia('(prefers-color-scheme: dark)');
  const currentTheme = () => {
    let saved = null;
    try { saved = window.localStorage.getItem('donut-theme'); } catch {}
    return saved === 'dark' || (saved !== 'light' && scheme.matches) ? 'dark' : 'light';
  };
  const update = () => {
    const theme = currentTheme();
    document.querySelectorAll('[data-theme-light][data-theme-dark]').forEach((element) => {
      const attribute = element.tagName === 'META' ? 'content' : 'href';
      element.setAttribute(attribute, element.getAttribute(`data-theme-${theme}`));
    });
  };
  update();
  scheme.addEventListener?.('change', update);
  document.addEventListener('click', (event) => {
    if (event.target.closest('.theme-toggle, .hub-theme-toggle')) requestAnimationFrame(update);
  });
  window.addEventListener('storage', (event) => {
    if (event.key === 'donut-theme') update();
  });
})();
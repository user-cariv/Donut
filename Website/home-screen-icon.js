(() => {
  const scheme = window.matchMedia('(prefers-color-scheme: dark)');
  const currentTheme = () => scheme.matches ? 'dark' : 'light';
  const update = () => {
    const theme = currentTheme();
    document.querySelectorAll('[data-theme-light][data-theme-dark]').forEach((element) => {
      const attribute = element.tagName === 'META' ? 'content' : 'href';
      element.setAttribute(attribute, element.getAttribute(`data-theme-${theme}`));
    });
  };
  update();
  scheme.addEventListener?.('change', update);
})();

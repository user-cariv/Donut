(() => {
  const root = document.documentElement;
  const themeToggle = document.getElementById('theme');
  const isDark = () => root.getAttribute('data-theme') === 'dark';
  const paintThemeLabel = () => themeToggle?.setAttribute('aria-label', isDark() ? 'Switch to light mode' : 'Switch to dark mode');
  paintThemeLabel();

  const island = document.getElementById('island');
  const content = document.getElementById('inner');
  const tabs = [...document.querySelectorAll('.chips button')];
  const scenes = {
    calendar: { height: 128, icon: '📅', iconClass: 'scene-calendar', title: 'Design review', detail: 'In 10 minutes · 3:30 PM' },
    message: { height: 128, icon: '💬', iconClass: 'scene-message', title: 'Maya', detail: 'Still on for dinner tonight?' },
    call: { height: 152, icon: '📞', iconClass: 'scene-call', title: 'Dad', detail: 'iPhone call', actions: ['Decline', 'Accept'] },
  };
  const order = ['message', 'calendar', 'call'];
  let index = 0;
  let timer = null;
  let current = null;

  function makeTextElement(tagName, className, text) {
    const element = document.createElement(tagName);
    if (className) element.className = className;
    element.textContent = text;
    return element;
  }

  function showScene(key) {
    const scene = scenes[key];
    if (!scene || !island || !content) return;
    tabs.forEach((button) => button.setAttribute('aria-pressed', String(button.dataset.s === key)));
    current = key;

    const row = document.createElement('div');
    row.className = 'row';
    row.append(makeTextElement('div', `ico ${scene.iconClass}`, scene.icon));
    const copy = document.createElement('div');
    copy.append(makeTextElement('b', '', scene.title));
    copy.append(makeTextElement('div', 'sub', scene.detail));
    row.append(copy);
    content.replaceChildren(row);

    if (scene.actions) {
      const actions = document.createElement('div');
      actions.className = 'acts';
      scene.actions.forEach((label, actionIndex) => {
        actions.append(makeTextElement('span', actionIndex === 0 ? 'scene-decline' : 'scene-accept', label));
      });
      content.append(actions);
    }

    island.classList.add('open');
    island.style.width = `${Math.min(360, island.parentElement.clientWidth - 24)}px`;
    island.style.height = `${scene.height}px`;
  }

  function resetScene() {
    current = null;
    island?.classList.remove('open');
    if (island) {
      island.style.width = '150px';
      island.style.height = '30px';
    }
    tabs.forEach((button) => button.setAttribute('aria-pressed', 'false'));
  }

  function cycleScenes() {
    showScene(order[index % order.length]);
    index += 1;
    timer = window.setTimeout(() => {
      resetScene();
      timer = window.setTimeout(cycleScenes, 900);
    }, 3200);
  }

  tabs.forEach((button) => button.addEventListener('click', () => {
    window.clearTimeout(timer);
    current === button.dataset.s ? resetScene() : showScene(button.dataset.s);
  }));
  window.addEventListener('resize', () => { if (current) showScene(current); }, { passive: true });
  if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) timer = window.setTimeout(cycleScenes, 900);
  else showScene('message');
})();

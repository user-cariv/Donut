(() => {
  const sessionKey = "donut-preview-account";
  const buttons = [];
  const header = document.querySelector("header");
  if (!header) return;

  const trigger = document.createElement("button");
  trigger.type = "button";
  trigger.className = "account-trigger";
  trigger.setAttribute("aria-haspopup", "dialog");
  trigger.setAttribute("aria-label", "Open Donut account");
  trigger.textContent = "Account";
  const host = header.querySelector(".nav-actions, .tools");
  let themeControl = null;
  if (host) {
    themeControl = host.querySelector(".hub-theme-toggle, .theme-toggle, .tg");
    const downloadLink = host.querySelector(".nav-download");
    if (themeControl) {
      if (downloadLink) host.insertBefore(downloadLink, themeControl);
      themeControl.insertAdjacentElement("afterend", trigger);
    } else host.append(trigger);
  }
  else {
    themeControl = header.querySelector(".hub-theme-toggle");
    const navigation = header.querySelector("nav");
    if (themeControl) {
      const actions = document.createElement("div");
      actions.className = "account-actions";
      themeControl.parentNode.insertBefore(actions, themeControl);
      actions.append(themeControl, trigger);
    }
    else if (navigation) navigation.insertAdjacentElement("afterend", trigger);
    else header.append(trigger);
  }
  buttons.push(trigger);

  const layer = document.createElement("div");
  layer.className = "account-layer";
  layer.hidden = true;
  layer.innerHTML = `
    <section class="account-dialog" role="dialog" aria-modal="true" aria-labelledby="account-title">
      <button class="account-close" type="button" aria-label="Close account dialog">×</button>
      <p class="account-eyebrow">Your Donut account</p>
      <h2 class="account-title" id="account-title">One account, every Donut.</h2>
      <p class="account-copy">Save your place and keep future purchases together in one Donut account.</p>
      <p class="account-preview-note"><strong>Private preview:</strong> sign-in is simulated in this browser. No password is requested, and nothing is sent to a server.</p>
      <div class="account-tabs" role="tablist" aria-label="Account options">
        <button class="account-tab" id="account-tab-signin" type="button" role="tab" aria-selected="true" aria-controls="account-panel-signin">Sign in</button>
        <button class="account-tab" id="account-tab-create" type="button" role="tab" aria-selected="false" aria-controls="account-panel-create">Create account</button>
      </div>
      <div class="account-panel" id="account-panel-signin" role="tabpanel" aria-labelledby="account-tab-signin">
        <form data-account-form="signin">
          <label class="account-label" for="account-email-signin">Email address</label>
          <input class="account-input" id="account-email-signin" name="email" type="email" autocomplete="email" placeholder="you@example.com" required>
          <button class="account-primary" type="submit">Continue with email</button>
          <p class="account-divider">or</p>
          <button class="account-google" type="button" data-preview-google><span>G</span> Continue with Google</button>
          <p class="account-message" aria-live="polite"></p>
        </form>
      </div>
      <div class="account-panel" id="account-panel-create" role="tabpanel" aria-labelledby="account-tab-create" hidden>
        <form data-account-form="create">
          <label class="account-label" for="account-name-create">Full name</label>
          <input class="account-input" id="account-name-create" name="name" autocomplete="name" placeholder="Your name" required>
          <label class="account-label" for="account-email-create">Email address</label>
          <input class="account-input" id="account-email-create" name="email" type="email" autocomplete="email" placeholder="you@example.com" required>
          <button class="account-primary" type="submit">Create preview account</button>
          <p class="account-divider">or</p>
          <button class="account-google" type="button" data-preview-google><span>G</span> Continue with Google</button>
          <p class="account-message" aria-live="polite"></p>
        </form>
      </div>
      <p class="account-footnote">Before launch, account security and Google authorization will be connected to Donut's private backend.</p>
    </section>`;
  document.body.append(layer);

  const dialog = layer.querySelector(".account-dialog");
  const closeButton = layer.querySelector(".account-close");
  const tabs = [...layer.querySelectorAll("[role=tab]")];
  const panels = [...layer.querySelectorAll("[role=tabpanel]")];
  const readAccount = () => {
    try { return JSON.parse(sessionStorage.getItem(sessionKey) || "null"); }
    catch { return null; }
  };
  const updateTriggers = () => {
    const user = readAccount();
    buttons.forEach((button) => {
      button.textContent = user ? (user.name || user.email.split("@")[0]) : "Account";
      button.dataset.signedIn = String(Boolean(user));
      button.setAttribute("aria-label", user ? `Preview account for ${user.email}` : "Open Donut account");
    });
  };
  const setTab = (name) => {
    tabs.forEach((tab) => {
      const selected = tab.id === `account-tab-${name}`;
      tab.setAttribute("aria-selected", String(selected));
      tab.tabIndex = selected ? 0 : -1;
    });
    panels.forEach((panel) => { panel.hidden = panel.id !== `account-panel-${name}`; });
  };
  const open = (tab = "signin") => {
    layer.hidden = false;
    document.body.style.overflow = "hidden";
    setTab(readAccount() ? "profile" : tab);
    if (readAccount()) showProfile();
    else layer.querySelector(`#account-panel-${tab} input`)?.focus();
  };
  const close = () => {
    layer.hidden = true;
    document.body.style.overflow = "";
  };
  const showProfile = () => {
    const user = readAccount();
    if (!user) return;
    layer.querySelector(".account-title").textContent = `Welcome${user.name ? `, ${user.name.split(" ")[0]}` : " back"}.`;
    layer.querySelector(".account-copy").textContent = "This preview account is available as you move between Donut pages in this browser session.";
    layer.querySelector(".account-preview-note").innerHTML = `<strong>Preview account:</strong> ${escapeText(user.email)}. This is a local demo identity, not a live Donut account.`;
    layer.querySelector(".account-tabs").hidden = true;
    panels.forEach((panel) => { panel.hidden = true; });
    let card = layer.querySelector(".account-user-card");
    if (!card) {
      card = document.createElement("div");
      card.className = "account-user-card";
      card.innerHTML = '<span class="account-avatar" aria-hidden="true"></span><span><strong></strong><small></small></span>';
      dialog.insertBefore(card, layer.querySelector(".account-footnote"));
      const signout = document.createElement("button");
      signout.type = "button";
      signout.className = "account-secondary";
      signout.textContent = "Sign out of preview";
      signout.addEventListener("click", () => {
        sessionStorage.removeItem(sessionKey);
        layer.querySelector(".account-title").textContent = "One account, every Donut.";
        layer.querySelector(".account-copy").textContent = "Save your place and keep future purchases together in one Donut account.";
        layer.querySelector(".account-preview-note").innerHTML = "<strong>Private preview:</strong> sign-in is simulated in this browser. No password is requested, and nothing is sent to a server.";
        layer.querySelector(".account-tabs").hidden = false;
        card.remove(); signout.remove(); updateTriggers(); setTab("signin");
      });
      dialog.insertBefore(signout, layer.querySelector(".account-footnote"));
    }
    card.querySelector(".account-avatar").textContent = (user.name || user.email).slice(0, 1).toUpperCase();
    card.querySelector("strong").textContent = user.name || "Donut member";
    card.querySelector("small").textContent = user.email;
  };
  function escapeText(value) {
    return String(value).replace(/[&<>"']/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[character]);
  }
  const finishPreviewSignIn = (name, email, form) => {
    sessionStorage.setItem(sessionKey, JSON.stringify({ name: name.trim(), email: email.trim().toLowerCase() }));
    form.querySelector(".account-message").textContent = "Preview sign-in complete. No account was created on a server.";
    updateTriggers();
    window.setTimeout(showProfile, 450);
  };

  trigger.addEventListener("click", () => open());
  closeButton.addEventListener("click", close);
  layer.addEventListener("click", (event) => { if (event.target === layer) close(); });
  document.addEventListener("keydown", (event) => { if (event.key === "Escape" && !layer.hidden) close(); });
  tabs.forEach((tab) => tab.addEventListener("click", () => setTab(tab.id.replace("account-tab-", ""))));
  layer.querySelectorAll("form[data-account-form]").forEach((form) => {
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const data = new FormData(form);
      const email = String(data.get("email") || "").trim();
      const name = String(data.get("name") || "").trim();
      const message = form.querySelector(".account-message");
      message.dataset.kind = "";
      finishPreviewSignIn(name, email, form);
    });
  });
  layer.querySelectorAll("[data-preview-google]").forEach((button) => button.addEventListener("click", () => {
    const email = layer.querySelector("#account-email-signin")?.value || layer.querySelector("#account-email-create")?.value;
    const form = button.closest("form");
    const message = form.querySelector(".account-message");
    if (!email) {
      message.textContent = "Preview mode: enter an email address first to demonstrate the signed-in state.";
      message.dataset.kind = "error";
      layer.querySelector("#account-email-signin")?.focus();
      return;
    }
    message.textContent = "Google authorization is not connected in this preview. Use the email button to try the account flow.";
    message.dataset.kind = "error";
  }));

  document.querySelectorAll("[data-support-form]").forEach((form) => {
    const account = readAccount();
    const fileInput = form.querySelector("#support-files");
    const fileList = form.querySelector(".hub-support-file-list");
    const readableSize = (bytes) => bytes < 1024 * 1024
      ? `${Math.max(1, Math.round(bytes / 1024))} KB`
      : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    const renderFiles = () => {
      if (!fileInput || !fileList) return;
      fileList.replaceChildren();
      [...fileInput.files].forEach((file, index) => {
        const item = document.createElement("li");
        const details = document.createElement("span");
        details.className = "hub-support-file-details";
        const name = document.createElement("strong");
        name.textContent = file.name;
        const size = document.createElement("small");
        size.textContent = readableSize(file.size);
        details.append(name, size);
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "hub-support-file-remove";
        remove.textContent = "Remove";
        remove.setAttribute("aria-label", `Remove ${file.name}`);
        remove.addEventListener("click", () => {
          const transfer = new DataTransfer();
          [...fileInput.files].forEach((selected, selectedIndex) => {
            if (selectedIndex !== index) transfer.items.add(selected);
          });
          fileInput.files = transfer.files;
          renderFiles();
        });
        item.append(details, remove);
        fileList.append(item);
      });
      fileList.hidden = fileList.children.length === 0;
    };
    fileInput?.addEventListener("change", renderFiles);
    if (account) {
      form.elements.name.value = account.name || "";
      form.elements.email.value = account.email || "";
    }
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      form.querySelector(".hub-support-message").textContent = "Thanks — the form works in preview. Support messages aren’t sent until Donut’s support service is connected.";
    });
    form.addEventListener("submit", () => {
      const count = fileInput?.files.length || 0;
      if (count) {
        window.setTimeout(() => {
          const message = form.querySelector(".hub-support-message");
          message.textContent += ` ${count} attachment${count === 1 ? "" : "s"} selected; files aren’t sent in this preview.`;
        }, 0);
      }
    });
  });
  const themeControls = [...document.querySelectorAll(".hub-theme-toggle, .theme-toggle, #theme")];
  const updateThemeLabels = () => {
    const isDark = document.body.classList.contains("dark-mode") || document.documentElement.getAttribute("data-theme") === "dark";
    themeControls.forEach((button) => {
      const nextTheme = isDark ? "light" : "dark";
      button.innerHTML = nextTheme === "dark"
        ? '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M20.985 12.486a9 9 0 1 1-9.473-9.472c.405-.022.617.46.402.804a6.5 6.5 0 0 0 8.268 8.268c.344-.215.826-.003.803.4Z"/></svg>'
        : '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="4"/><path d="M12 2v2m0 16v2M4.93 4.93l1.42 1.42m11.3 11.3 1.42 1.42M2 12h2m16 0h2M4.93 19.07l1.42-1.42m11.3-11.3 1.42-1.42"/></svg>';
      button.setAttribute("aria-label", `Switch to ${nextTheme} mode`);
      button.title = `Switch to ${nextTheme} mode`;
    });
  };
  updateThemeLabels();
  document.addEventListener("click", (event) => {
    if (event.target.closest(".hub-theme-toggle, .theme-toggle, #theme")) window.setTimeout(updateThemeLabels, 0);
  });
  updateTriggers();
})();

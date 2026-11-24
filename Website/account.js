(() => {
  const config = window.DONUT_SUPABASE_CONFIG || {};
  const supabaseClient = window.supabase?.createClient && config.url && config.publishableKey
    ? window.supabase.createClient(config.url, config.publishableKey, {
      auth: {
        autoRefreshToken: true,
        detectSessionInUrl: true,
        persistSession: true,
        storageKey: "donut-auth",
      },
    })
    : null;
  let authUser = null;
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
      <p class="account-preview-note">Sign in with your Donut account email and password. Your account follows you across Donut pages.</p>
      <div class="account-tabs" role="tablist" aria-label="Account options">
        <button class="account-tab" id="account-tab-signin" type="button" role="tab" aria-selected="true" aria-controls="account-panel-signin">Sign in</button>
        <button class="account-tab" id="account-tab-create" type="button" role="tab" aria-selected="false" aria-controls="account-panel-create">Create account</button>
      </div>
      <div class="account-panel" id="account-panel-signin" role="tabpanel" aria-labelledby="account-tab-signin">
        <form data-account-form="signin">
          <label class="account-label" for="account-email-signin">Email address</label>
          <input class="account-input" id="account-email-signin" name="email" type="email" autocomplete="email" placeholder="you@example.com" required>
          <label class="account-label" for="account-password-signin">Password</label>
          <input class="account-input" id="account-password-signin" name="password" type="password" autocomplete="current-password" required>
          <button class="account-reset-link" type="button" data-reset-password>Forgot password?</button>
          <button class="account-primary" type="submit">Continue with email</button>
          <p class="account-message" aria-live="polite"></p>
        </form>
      </div>
      <div class="account-panel" id="account-panel-create" role="tabpanel" aria-labelledby="account-tab-create" hidden>
        <form data-account-form="create">
          <label class="account-label" for="account-name-create">Full name</label>
          <input class="account-input" id="account-name-create" name="name" autocomplete="name" placeholder="Your name" required>
          <label class="account-label" for="account-email-create">Email address</label>
          <input class="account-input" id="account-email-create" name="email" type="email" autocomplete="email" placeholder="you@example.com" required>
          <label class="account-label" for="account-password-create">Password</label>
          <input class="account-input" id="account-password-create" name="password" type="password" autocomplete="new-password" minlength="8" required>
          <button class="account-primary" type="submit">Create account</button>
          <p class="account-message" aria-live="polite"></p>
        </form>
      </div>
      <div class="account-panel" id="account-panel-reset" hidden>
        <form data-password-reset-form>
          <label class="account-label" for="account-password-new">New password</label>
          <input class="account-input" id="account-password-new" name="password" type="password" autocomplete="new-password" minlength="8" required>
          <button class="account-primary" type="submit">Save new password</button>
          <p class="account-message" aria-live="polite"></p>
        </form>
      </div>
    </section>`;
  document.body.append(layer);

  const dialog = layer.querySelector(".account-dialog");
  const closeButton = layer.querySelector(".account-close");
  const tabs = [...layer.querySelectorAll("[role=tab]")];
  const panels = [...layer.querySelectorAll("[role=tabpanel]")];
  const displayName = (user) => user?.user_metadata?.full_name || user?.user_metadata?.name || user?.email?.split("@")[0] || "Donut member";
  const getSafeRedirect = () => {
    const allowedOrigins = new Set(["https://user-cariv.github.io", "http://127.0.0.1:4173", "http://localhost:4173"]);
    const origin = allowedOrigins.has(window.location.origin) ? window.location.origin : "https://user-cariv.github.io";
    const path = origin === "https://user-cariv.github.io" ? "/Donut/" : "/";
    return `${origin}${path}`;
  };
  const readAccount = () => authUser ? { name: displayName(authUser), email: authUser.email || "" } : null;
  const updateTriggers = () => {
    const user = readAccount();
    buttons.forEach((button) => {
      button.textContent = user ? (user.name || user.email.split("@")[0]) : "Account";
      button.dataset.signedIn = String(Boolean(user));
      button.setAttribute("aria-label", user ? `Account for ${user.email}` : "Open Donut account");
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
    setTab(readAccount() ? "signin" : tab);
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
    layer.querySelector(".account-copy").textContent = "Your account is ready across the Donut website.";
    layer.querySelector(".account-preview-note").textContent = `Signed in securely as ${user.email}.`;
    layer.querySelector(".account-tabs").hidden = true;
    panels.forEach((panel) => { panel.hidden = true; });
    let card = layer.querySelector(".account-user-card");
    if (!card) {
      card = document.createElement("div");
      card.className = "account-user-card";
      card.innerHTML = '<span class="account-avatar" aria-hidden="true"></span><span><strong></strong><small></small></span>';
      dialog.append(card);
      const signout = document.createElement("button");
      signout.type = "button";
      signout.className = "account-secondary";
      signout.textContent = "Sign out";
      signout.addEventListener("click", async () => {
        signout.disabled = true;
        const { error } = await supabaseClient.auth.signOut({ scope: "global" });
        if (error) {
          layer.querySelector(".account-preview-note").textContent = error.message;
          signout.disabled = false;
        }
      });
      dialog.append(signout);
    }
    card.querySelector(".account-avatar").textContent = user.name.slice(0, 1).toUpperCase();
    card.querySelector("strong").textContent = user.name || "Donut member";
    card.querySelector("small").textContent = user.email;
  };
  const resetProfile = () => {
    layer.querySelector(".account-user-card")?.remove();
    layer.querySelector(".account-secondary")?.remove();
    layer.querySelector(".account-title").textContent = "One account, every Donut.";
    layer.querySelector(".account-copy").textContent = "Save your place and keep future purchases together in one Donut account.";
    layer.querySelector(".account-preview-note").textContent = "Sign in with your Donut account email and password. Your account follows you across Donut pages.";
    layer.querySelector(".account-tabs").hidden = false;
    panels.forEach((panel) => { panel.hidden = panel.id !== "account-panel-signin"; });
  };

  trigger.addEventListener("click", () => open());
  closeButton.addEventListener("click", close);
  layer.addEventListener("click", (event) => { if (event.target === layer) close(); });
  document.addEventListener("keydown", (event) => { if (event.key === "Escape" && !layer.hidden) close(); });
  tabs.forEach((tab) => tab.addEventListener("click", () => setTab(tab.id.replace("account-tab-", ""))));
  layer.querySelectorAll("form[data-account-form]").forEach((form) => {
    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      const data = new FormData(form);
      const email = String(data.get("email") || "").trim();
      const name = String(data.get("name") || "").trim();
      const password = String(data.get("password") || "");
      const message = form.querySelector(".account-message");
      const submit = form.querySelector('button[type="submit"]');
      message.dataset.kind = "";
      if (!supabaseClient) {
        message.textContent = "Donut sign-in could not load. Refresh the page and try again.";
        message.dataset.kind = "error";
        return;
      }
      const originalText = submit.textContent;
      submit.disabled = true;
      submit.textContent = form.dataset.accountForm === "create" ? "Creating account…" : "Signing in…";
      message.textContent = "";
      try {
        const redirectTo = getSafeRedirect();
        const result = form.dataset.accountForm === "create"
          ? await supabaseClient.auth.signUp({
            email,
            password,
            options: { data: { full_name: name }, emailRedirectTo: redirectTo },
          })
          : await supabaseClient.auth.signInWithPassword({ email, password });
        if (result.error) throw result.error;
        if (form.dataset.accountForm === "create" && !result.data.session) {
          message.textContent = "Check your email for a confirmation link to finish creating your account.";
        } else {
          message.textContent = "You’re signed in.";
          updateTriggers();
          showProfile();
        }
      } catch (error) {
        message.textContent = form.dataset.accountForm === "create"
          ? "We couldn’t create the account. Check the details or try signing in if you may already have an account."
          : "Email or password is incorrect, or sign-in is temporarily unavailable.";
        message.dataset.kind = "error";
      } finally {
        submit.disabled = false;
        submit.textContent = originalText;
      }
    });
  });
  layer.querySelectorAll("[data-reset-password]").forEach((button) => button.addEventListener("click", async () => {
    const form = button.closest("form");
    const message = form?.querySelector(".account-message");
    const email = String(form?.elements.email?.value || "").trim();
    if (!supabaseClient || !email) {
      message.textContent = "Enter your email address, then request a password reset.";
      message.dataset.kind = "error";
      return;
    }
    button.disabled = true;
    try {
      const { error } = await supabaseClient.auth.resetPasswordForEmail(email, { redirectTo: getSafeRedirect() });
      if (error) throw error;
      // Same response whether the account exists or not.
      message.textContent = "If an account matches that email, password reset instructions will arrive shortly.";
      message.dataset.kind = "";
    } catch {
      message.textContent = "Password reset is temporarily unavailable. Please try again later.";
      message.dataset.kind = "error";
    } finally {
      button.disabled = false;
    }
  }));
  layer.querySelector("[data-password-reset-form]")?.addEventListener("submit", async (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    const message = form.querySelector(".account-message");
    const submit = form.querySelector('button[type="submit"]');
    submit.disabled = true;
    try {
      const { error } = await supabaseClient.auth.updateUser({ password: String(new FormData(form).get("password") || "") });
      if (error) throw error;
      await supabaseClient.auth.signOut({ scope: "global" });
      resetProfile();
      message.textContent = "Password updated. Sign in with your new password.";
      message.dataset.kind = "";
      setTab("signin");
    } catch {
      message.textContent = "Could not update the password. The link may have expired; request another reset email.";
      message.dataset.kind = "error";
    } finally {
      submit.disabled = false;
    }
  });
  if (supabaseClient) {
    const callbackParams = new URLSearchParams(window.location.search);
    const hashParams = new URLSearchParams(window.location.hash.slice(1));
    const returningFromAuthLink = callbackParams.has("code");
    supabaseClient.auth.onAuthStateChange((event, session) => {
      authUser = session?.user || null;
      updateTriggers();
      if (event === "PASSWORD_RECOVERY") {
        layer.hidden = false;
        document.body.style.overflow = "hidden";
        layer.querySelector(".account-title").textContent = "Choose a new password.";
        layer.querySelector(".account-copy").textContent = "Use a new password for your Donut account.";
        layer.querySelector(".account-preview-note").textContent = "Your reset link is verified. Set a new password to continue.";
        layer.querySelector(".account-tabs").hidden = true;
        panels.forEach((panel) => { panel.hidden = true; });
        layer.querySelector("#account-panel-reset").hidden = false;
        layer.querySelector("#account-password-new")?.focus();
      } else if (authUser && returningFromAuthLink) {
        open();
        showProfile();
      } else if (event === "SIGNED_IN" && !layer.hidden) {
        showProfile();
      } else if (event === "SIGNED_OUT") {
        resetProfile();
      }
    });
    const authError = callbackParams.get("error_description") || hashParams.get("error_description");
    if (authError) {
      open("signin");
      const message = layer.querySelector("#account-panel-signin .account-message");
      message.textContent = "This account link could not be verified. Please try again.";
      message.dataset.kind = "error";
    }
  }

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
  window.addEventListener("donut-theme-change", updateThemeLabels);
  document.addEventListener("click", (event) => {
    if (event.target.closest(".hub-theme-toggle, .theme-toggle, #theme")) window.setTimeout(updateThemeLabels, 0);
  });
  updateTriggers();
})();

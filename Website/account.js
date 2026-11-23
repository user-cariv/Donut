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
      <p class="account-preview-note">Sign in with Google or your Donut email and password. Your account follows you across Donut pages.</p>
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
          <p class="account-divider">or</p>
          <button class="account-google" type="button" data-google-auth>
            <svg aria-hidden="true" viewBox="0 0 48 48"><circle cx="24" cy="24" r="23" fill="#fff"/><path fill="#4285F4" d="M43.6 24.5c0-1.4-.1-2.8-.4-4.1H24v7.8h11.1c-.5 2.5-1.9 4.7-4.1 6.2v5.1h6.6c3.9-3.6 6-8.9 6-15z"/><path fill="#34A853" d="M24 44c5.5 0 10.1-1.8 13.5-4.9l-6.6-5.1c-1.8 1.2-4.1 2-6.9 2-5.3 0-9.8-3.6-11.4-8.4H5.8v5.3C9.2 39.5 16 44 24 44z"/><path fill="#FBBC05" d="M12.6 27.6a12 12 0 0 1 0-7.2v-5.3H5.8a20 20 0 0 0 0 17.8l6.8-5.3z"/><path fill="#EA4335" d="M24 12c3 0 5.7 1 7.8 3.1l5.9-5.9C34.1 5.8 29.5 4 24 4 16 4 9.2 8.5 5.8 15.1l6.8 5.3C14.2 15.6 18.7 12 24 12z"/></svg>
            Continue with Google
          </button>
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
          <p class="account-divider">or</p>
          <button class="account-google" type="button" data-google-auth>
            <svg aria-hidden="true" viewBox="0 0 48 48"><circle cx="24" cy="24" r="23" fill="#fff"/><path fill="#4285F4" d="M43.6 24.5c0-1.4-.1-2.8-.4-4.1H24v7.8h11.1c-.5 2.5-1.9 4.7-4.1 6.2v5.1h6.6c3.9-3.6 6-8.9 6-15z"/><path fill="#34A853" d="M24 44c5.5 0 10.1-1.8 13.5-4.9l-6.6-5.1c-1.8 1.2-4.1 2-6.9 2-5.3 0-9.8-3.6-11.4-8.4H5.8v5.3C9.2 39.5 16 44 24 44z"/><path fill="#FBBC05" d="M12.6 27.6a12 12 0 0 1 0-7.2v-5.3H5.8a20 20 0 0 0 0 17.8l6.8-5.3z"/><path fill="#EA4335" d="M24 12c3 0 5.7 1 7.8 3.1l5.9-5.9C34.1 5.8 29.5 4 24 4 16 4 9.2 8.5 5.8 15.1l6.8 5.3C14.2 15.6 18.7 12 24 12z"/></svg>
            Continue with Google
          </button>
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
      <p class="account-footnote">Google sign-in is secure and continues across the Donut website.</p>
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
      dialog.insertBefore(card, layer.querySelector(".account-footnote"));
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
      dialog.insertBefore(signout, layer.querySelector(".account-footnote"));
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
    layer.querySelector(".account-preview-note").textContent = "Sign in with Google or your Donut email and password. Your account follows you across Donut pages.";
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
  layer.querySelectorAll("[data-google-auth]").forEach((button) => button.addEventListener("click", async () => {
    const form = button.closest("form");
    const message = form.querySelector(".account-message");
    if (!supabaseClient) {
      message.textContent = "Donut sign-in could not load. Refresh the page and try again.";
      message.dataset.kind = "error";
      return;
    }
    button.disabled = true;
    message.dataset.kind = "";
    message.textContent = "Connecting securely to Google…";
    try {
      const redirectTo = getSafeRedirect();
      const { error } = await supabaseClient.auth.signInWithOAuth({
        provider: "google",
        options: {
          redirectTo,
          queryParams: { prompt: "select_account" },
        },
      });
      if (error) throw error;
    } catch (error) {
      message.textContent = "Google sign-in could not start. Please try again.";
      message.dataset.kind = "error";
      button.disabled = false;
    }
  }));

  if (supabaseClient) {
    const callbackParams = new URLSearchParams(window.location.search);
    const hashParams = new URLSearchParams(window.location.hash.slice(1));
    const returningFromOAuth = callbackParams.has("code");
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
      } else if (authUser && returningFromOAuth) {
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
      message.textContent = "Google sign-in did not complete. Please try again.";
      message.dataset.kind = "error";
    }
  }

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
      const supportMessage = form.querySelector(".hub-support-message");
      const supportConfig = window.DONUT_SUPABASE_CONFIG || {};
      if (supportMessage && supportConfig.url && supportConfig.publishableKey) {
        supportMessage.textContent = "Support requests are ready. Email alerts will be enabled after the support inbox is set up.";
      }
      form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const message = supportMessage;
        const submit = form.querySelector('button[type="submit"]');
        const config = window.DONUT_SUPABASE_CONFIG || {};
        if (!config.url || !config.publishableKey) {
          message.textContent = "Support isn’t connected yet. Your request and files were not sent.";
          return;
        }
        const selectedFiles = [...(fileInput?.files || [])];
        const allowedTypes = new Set(["image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf"]);
        const totalBytes = selectedFiles.reduce((sum, file) => sum + file.size, 0);
        if (selectedFiles.length > 5 || totalBytes > 20 * 1024 * 1024 ||
            selectedFiles.some((file) => file.size > 8 * 1024 * 1024 || !allowedTypes.has(file.type))) {
          message.textContent = "Attach up to 5 JPG, PNG, WebP, GIF, or PDF files, each under 8 MB and 20 MB total.";
          return;
        }
        const originalLabel = submit?.textContent || "Send a support request";
        if (submit) {
          submit.disabled = true;
          submit.textContent = "Sending…";
        }
        message.textContent = "Sending your request securely…";
        try {
          const endpoint = `${config.url.replace(/\/$/, "")}/functions/v1/submit-support`;
          const response = await fetch(endpoint, {
            method: "POST",
            headers: {
              apikey: config.publishableKey,
              Authorization: `Bearer ${config.publishableKey}`,
            },
            body: new FormData(form),
          });
          const result = await response.json().catch(() => ({}));
          if (!response.ok) throw new Error(result.error || "We couldn’t send your request. Please try again.");
          const shortId = String(result.ticketId || "").slice(0, 8).toUpperCase();
          message.textContent = shortId
            ? `Your support request was sent successfully. Reference ${shortId}.`
            : "Your support request was sent successfully.";
          form.elements.issue.value = "";
          if (fileInput) fileInput.value = "";
          renderFiles();
        } catch (error) {
          message.textContent = error instanceof Error ? error.message : "We couldn’t send your request. Please try again.";
        } finally {
          if (submit) {
            submit.disabled = false;
            submit.textContent = originalLabel;
          }
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
  window.addEventListener("donut-theme-change", updateThemeLabels);
  document.addEventListener("click", (event) => {
    if (event.target.closest(".hub-theme-toggle, .theme-toggle, #theme")) window.setTimeout(updateThemeLabels, 0);
  });
  updateTriggers();
})();

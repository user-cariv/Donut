const form = document.getElementById("send-link");
const feedback = document.getElementById("status");
const submit = document.getElementById("send-button");

form.addEventListener("submit", async event => {
  event.preventDefault();
  submit.disabled = true;
  feedback.textContent = "Opening Donut Boost…";
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    await sendToDonutBoost(document.getElementById("download-link").value, tab?.id);
    feedback.textContent = "Review the link in Donut Boost and click Boost Download.";
  } catch (error) {
    feedback.textContent = error.message;
  } finally {
    submit.disabled = false;
  }
});

document.getElementById("reload-extension").addEventListener("click", () => chrome.runtime.reload());

const signedInButton = document.getElementById("send-signed-in");
signedInButton.addEventListener("click", async () => {
  if (!form.reportValidity()) return;
  let url;
  try {
    url = new URL(document.getElementById("download-link").value.trim());
    if (url.protocol !== "https:" || url.username || url.password || url.href.length > 8192 ||
        url.pathname.toLowerCase().endsWith(".torrent")) throw new Error();
  } catch {
    feedback.textContent = "Use a direct HTTPS file link from the site you're signed in to.";
    return;
  }

  const hostAccess = { origins: [`https://${url.hostname}/*`] };
  signedInButton.disabled = true;
  try {
    const allowed = await chrome.permissions.request({ ...hostAccess, permissions: ["cookies", "nativeMessaging"] });
    if (!allowed) throw new Error("The browser needs your permission for that site.");
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab?.incognito) throw new Error("Keep private browsing downloads in the browser.");

    const cookies = await chrome.cookies.getAll({ url: url.href });
    const cookieHeader = cookies
      .filter(cookie => /^[!#$%&'*+\-.^_`|~\w]+$/.test(cookie.name) &&
        /^[\x21\x23-\x2B\x2D-\x3A\x3C-\x5B\x5D-\x7E]*$/.test(cookie.value))
      .map(cookie => `${cookie.name}=${cookie.value}`).join("; ");
    if (!cookieHeader || cookieHeader.length > 4096)
      throw new Error("This link does not have a supported browser sign-in session.");
    const response = await chrome.runtime.sendNativeMessage("com.donutboost.browser", {
      action: "authenticated", id: crypto.randomUUID(), url: url.href, cookieHeader
    });
    if (response?.status !== "accepted") throw new Error("Open the licensed Donut Boost app and try again.");
    feedback.textContent = "Added to Donut Boost’s download queue.";
  } catch (error) {
    feedback.textContent = error.message;
  } finally {
    await chrome.permissions.remove(hostAccess).catch(() => {});
    signedInButton.disabled = false;
  }
});

const captureForm = document.getElementById("capture-form");
const captureEnabled = document.getElementById("capture-enabled");
const captureMinimum = document.getElementById("capture-minimum");
const captureExclusions = document.getElementById("capture-exclusions");
const captureFeedback = document.getElementById("capture-feedback");

chrome.storage.local.get("captureSettings").then(({ captureSettings }) => {
  if (!captureSettings) return;
  captureEnabled.checked = captureSettings.enabled;
  captureMinimum.value = captureSettings.minimumMb;
  captureExclusions.value = captureSettings.excludedSites.join("\n");
}).catch(() => { captureFeedback.textContent = "Could not read capture settings."; });

captureForm.addEventListener("submit", async event => {
  event.preventDefault();
  const excludedSites = [...new Set(captureExclusions.value.split(/\r?\n/)
    .map(site => site.trim().toLowerCase().replace(/\.$/, "")).filter(Boolean))];
  if (excludedSites.some(site => !/^[a-z0-9-]+(?:\.[a-z0-9-]+)+$/.test(site))) {
    captureFeedback.textContent = "Enter domain names such as example.com, without paths or https://.";
    return;
  }

  try {
    const enabled = captureEnabled.checked;
    if (enabled && !await chrome.permissions.request({ permissions: ["downloads", "nativeMessaging"] })) {
      captureEnabled.checked = false;
      captureFeedback.textContent = "Allow browser downloads and app access to enable capture.";
      return;
    }
    await chrome.storage.local.set({ captureSettings: { enabled, minimumMb: captureMinimum.valueAsNumber, excludedSites } });
    captureFeedback.textContent = enabled
      ? "Capture is enabled when Donut Boost is open. The browser resumes downloads the app cannot finish."
      : "Automatic capture is off.";
  } catch {
    captureFeedback.textContent = "Could not save capture settings.";
  }
});

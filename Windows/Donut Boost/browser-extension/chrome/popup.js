const form = document.getElementById("send-link");
const feedback = document.getElementById("status");
const submit = document.getElementById("send-button");

form.addEventListener("submit", async event => {
  event.preventDefault();
  submit.disabled = true;
  feedback.textContent = "Opening Donut Boost…";
  try {
    await sendToDonutBoost(document.getElementById("download-link").value);
    feedback.textContent = "Review the link in Donut Boost and click Boost Download.";
  } catch (error) {
    feedback.textContent = error.message;
  } finally {
    submit.disabled = false;
  }
});

document.getElementById("reload-extension").addEventListener("click", () => chrome.runtime.reload());

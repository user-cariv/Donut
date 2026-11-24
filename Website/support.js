const copyButton = document.querySelector("#ds-copy-btn");
const supportAddress = document.querySelector("#ds-email-address");
const copyStatus = document.querySelector("#support .ds-copy-status");
let resetCopyFeedback;

copyButton?.addEventListener("click", async () => {
  const address = supportAddress.textContent.trim();
  try {
    await navigator.clipboard.writeText(address);
    copyButton.textContent = "Copied!";
    copyButton.classList.add("done");
    copyStatus.textContent = "Support email address copied.";
  } catch {
    const selection = window.getSelection();
    if (selection) {
      const range = document.createRange();
      range.selectNodeContents(supportAddress);
      selection.removeAllRanges();
      selection.addRange(range);
    }
    copyStatus.textContent = "Copy the highlighted address, or use Email Donut Support.";
  }
  clearTimeout(resetCopyFeedback);
  resetCopyFeedback = setTimeout(() => {
    copyButton.textContent = "Copy";
    copyButton.classList.remove("done");
  }, 1600);
});

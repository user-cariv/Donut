(() => {
  const currencySelect = document.getElementById('boost-currency');
  const status = document.getElementById('boost-fx-status');
  const amounts = [...document.querySelectorAll('.boost-price-amount[data-inr]')];
  if (!currencySelect || !status || !amounts.length) return;

  const endpoint = 'https://open.er-api.com/v6/latest/INR';
  const currencyKey = 'donut-boost-currency';
  const selectedRateKey = 'donut-boost-selected-fx';
  const maxAge = 24 * 60 * 60 * 1000;
  let requestId = 0;

  try {
    const savedCurrency = localStorage.getItem(currencyKey);
    if (savedCurrency && [...currencySelect.options].some(option => option.value === savedCurrency)) {
      currencySelect.value = savedCurrency;
    }
  } catch (_) {}

  function format(amount, currency) {
    try {
      return new Intl.NumberFormat(undefined, {
        style: 'currency', currency, maximumFractionDigits: currency === 'INR' ? 0 : 2
      }).format(amount);
    } catch (_) {
      return `${currency} ${amount.toFixed(2)}`;
    }
  }

  function applyRate(currency, rate, updatedAt, cached = false) {
    amounts.forEach(element => {
      const inr = Number(element.dataset.inr);
      element.textContent = currency === 'INR' ? format(inr, currency) : format(inr * rate, currency);
    });
    if (currency === 'INR') {
      status.textContent = 'Prices are set in INR. Choose another currency for an estimate.';
      return;
    }
    const timestamp = updatedAt ? new Date(updatedAt).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' }) : '';
    status.textContent = `₹1 = ${format(rate, currency)} · Daily reference rate${timestamp ? ` · Updated ${timestamp}` : ''}${cached ? ' · Saved rate' : ''}`;
  }

  async function updatePrices() {
    const id = ++requestId;
    const currency = currencySelect.value;
    try { localStorage.setItem(currencyKey, currency); } catch (_) {}
    if (currency === 'INR') {
      applyRate(currency, 1);
      return;
    }

    let saved = null;
    try { saved = JSON.parse(localStorage.getItem(selectedRateKey) || 'null'); } catch (_) {}
    if (saved && saved.currency === currency && Number.isFinite(saved.rate) && Date.now() - saved.savedAt < maxAge) {
      applyRate(currency, saved.rate, saved.updatedAt, true);
      return;
    }

    status.textContent = 'Updating daily reference rate…';
    try {
      const response = await fetch(endpoint, { cache: 'no-store' });
      if (!response.ok) throw new Error('Exchange rate request failed');
      const data = await response.json();
      const rate = Number(data?.rates?.[currency]);
      if (data?.result !== 'success' || data?.base_code !== 'INR' || !Number.isFinite(rate) || rate <= 0) {
        throw new Error('Selected currency rate is unavailable');
      }
      if (id !== requestId) return;
      const record = { currency, rate, updatedAt: data.time_last_update_utc, savedAt: Date.now() };
      try { localStorage.setItem(selectedRateKey, JSON.stringify(record)); } catch (_) {}
      applyRate(currency, rate, record.updatedAt);
    } catch (_) {
      if (id !== requestId) return;
      if (saved && saved.currency === currency && Number.isFinite(saved.rate)) {
        applyRate(currency, saved.rate, saved.updatedAt, true);
        status.textContent += ' · Offline; using saved rate';
      } else {
        applyRate('INR', 1);
        status.textContent = 'Exchange rates are unavailable right now. Prices are shown in INR.';
      }
    }
  }

  currencySelect.addEventListener('change', updatePrices);
  updatePrices();
})();

/*
 * Pemilih tanggal seragam (Flatpickr) untuk seluruh aplikasi.
 * - Semua <input type="date"> & <input type="datetime-local"> otomatis diganti kalender Flatpickr.
 * - Yang TAMPIL ke pengguna: dd/mm/yyyy (dan HH:mm untuk tanggal+jam, format 24 jam).
 * - Yang DIKIRIM ke server tetap format ISO (yyyy-mm-dd / yyyy-mm-ddTHH:mm), jadi controller tidak berubah.
 * - Kalau JS mengubah nilai tanggal (tombol "7 hari", reset filter, isi form edit),
 *   pakai window.akpSetDate(el, 'yyyy-mm-dd') supaya tampilan ikut berubah.
 * - Tanggal boleh diketik langsung, mis. 17/05/1990.
 */
(function () {
    'use strict';

    if (!window.flatpickr) return;   // kalau CDN gagal dimuat, input bawaan browser tetap dipakai

    if (flatpickr.l10ns && flatpickr.l10ns.id) flatpickr.localize(flatpickr.l10ns.id);

    function init(el) {
        if (el._flatpickr) return;
        const withTime = el.type === 'datetime-local';

        flatpickr(el, {
            allowInput: true,
            disableMobile: true,          // tampilan sama di HP & PC (bukan picker bawaan HP)
            altInput: true,
            altFormat: withTime ? 'd/m/Y H:i' : 'd/m/Y',
            dateFormat: withTime ? 'Y-m-d\\TH:i' : 'Y-m-d',
            enableTime: withTime,
            time_24hr: true,
            minuteIncrement: 1,
            maxDate: el.dataset.maxToday === 'true' ? 'today' : null,
            onReady: (dates, str, fp) => {
                // Placeholder sesuai format, dan isi yang diketik langsung diproses saat keluar dari kolom
                fp.altInput.placeholder = el.getAttribute('placeholder') || (withTime ? 'dd/mm/yyyy jj:mm' : 'dd/mm/yyyy');
                fp.altInput.setAttribute('inputmode', 'numeric');
                if (el.id) fp.altInput.setAttribute('aria-labelledby', el.id);
            }
        });
    }

    document.querySelectorAll('input[type="date"], input[type="datetime-local"]').forEach(init);

    // Untuk tanggal yang diubah lewat JavaScript
    window.akpSetDate = function (el, value) {
        if (!el) return;
        if (el._flatpickr) {
            if (value) el._flatpickr.setDate(value, false);
            else el._flatpickr.clear(false);
        } else {
            el.value = value || '';
        }
    };
})();

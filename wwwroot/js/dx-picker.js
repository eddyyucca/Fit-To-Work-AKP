/*
 * Pemilih diagnosa dengan pencarian (autocomplete) untuk form Kunjungan Klinik.
 * Cari berdasarkan kode ICD-10 (mis. "N20" atau "n209"), nama Indonesia, nama Inggris, atau kategori.
 * Semua kata yang diketik harus cocok. Panah atas/bawah memilih, Enter menambah, Esc menutup.
 *
 * Pemakaian:
 *   AkpDxPicker.init({ input, list, data: [{ id, kode, name, en, kat, label }], isSelected(id), onSelect(item) });
 */
(function () {
    'use strict';

    const MAX_SHOWN = 40;

    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    // Menyorot kata yang cocok (pencarian tidak membedakan huruf besar/kecil)
    function highlight(text, tokens) {
        const raw = String(text ?? '');
        if (!tokens.length) return esc(raw);
        const lower = raw.toLowerCase();
        const marks = new Array(raw.length).fill(false);
        tokens.forEach((t) => {
            if (!t) return;
            let i = lower.indexOf(t);
            while (i >= 0) {
                for (let k = i; k < i + t.length; k++) marks[k] = true;
                i = lower.indexOf(t, i + t.length);
            }
        });
        let out = '', open = false;
        for (let i = 0; i < raw.length; i++) {
            if (marks[i] && !open) { out += '<mark class="p-0">'; open = true; }
            if (!marks[i] && open) { out += '</mark>'; open = false; }
            out += esc(raw[i]);
        }
        return open ? out + '</mark>' : out;
    }

    function init(opts) {
        const input = opts.input;
        const list = opts.list;
        const items = (opts.data || []).map((d) => {
            const kode = (d.kode || '').toLowerCase();
            const name = (d.name || '').toLowerCase();
            const en = (d.en || '').toLowerCase();
            const kat = (d.kat || '').toLowerCase();
            return {
                ...d,
                _kode: kode,
                _kodeFlat: kode.replace(/[.\-\s]/g, ''),
                _name: name,
                _hay: `${kode} ${kode.replace(/[.\-\s]/g, '')} ${name} ${en} ${kat}`
            };
        });

        let shown = [];
        let active = -1;

        const close = () => { list.classList.add('d-none'); list.innerHTML = ''; shown = []; active = -1; };

        function score(it, q, flat) {
            if (it._kode === q || it._kodeFlat === flat) return 0;
            if (it._kode.startsWith(q) || it._kodeFlat.startsWith(flat)) return 1;
            if (it._name.startsWith(q)) return 2;
            if (it._kode.includes(q) || it._kodeFlat.includes(flat)) return 3;
            if (new RegExp('(^|[\\s(/\\-])' + q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).test(it._name)) return 4;
            return 5;
        }

        function search(raw) {
            const q = raw.trim().toLowerCase();
            if (!q) return { rows: [], total: 0, tokens: [] };
            const tokens = q.split(/\s+/).filter(Boolean);
            const tokensFlat = tokens.map((t) => t.replace(/[.\-]/g, ''));
            const flat = q.replace(/[.\-\s]/g, '');
            const matched = items.filter((it) =>
                tokens.every((t, i) => it._hay.includes(t) || (tokensFlat[i] && it._hay.includes(tokensFlat[i]))));
            matched.sort((a, b) => score(a, q, flat) - score(b, q, flat) || a._kode.localeCompare(b._kode));
            return { rows: matched.slice(0, MAX_SHOWN), total: matched.length, tokens };
        }

        function render() {
            const { rows, total, tokens } = search(input.value);
            shown = rows;
            active = rows.length ? 0 : -1;
            if (!input.value.trim()) { close(); return; }
            if (!rows.length) {
                list.innerHTML = '<div class="list-group-item small text-muted">Tidak ada diagnosa yang cocok. Periksa ejaan, atau tambahkan di menu Jenis Penyakit.</div>';
                list.classList.remove('d-none');
                return;
            }
            list.innerHTML = rows.map((it, i) => {
                const picked = opts.isSelected && opts.isSelected(it.id);
                return `<button type="button" class="list-group-item list-group-item-action py-1 dx-item${i === active ? ' active' : ''}${picked ? ' disabled' : ''}" data-i="${i}">
                    <div class="d-flex justify-content-between gap-2">
                        <span><strong>${highlight(it.kode || '-', tokens)}</strong> &middot; ${highlight(it.name, tokens)}</span>
                        <span class="small ${i === active ? '' : 'text-muted'} text-nowrap">${picked ? '&#10003; dipilih' : esc(it.kat || '')}</span>
                    </div>
                    ${it.en ? `<div class="small ${i === active ? '' : 'text-muted'}">${highlight(it.en, tokens)}</div>` : ''}
                </button>`;
            }).join('') + (total > rows.length
                ? `<div class="list-group-item small text-muted">dan ${total - rows.length} lainnya &mdash; ketik lebih spesifik untuk mempersempit.</div>` : '');
            list.classList.remove('d-none');
        }

        function setActive(i) {
            if (!shown.length) return;
            active = (i + shown.length) % shown.length;
            list.querySelectorAll('.dx-item').forEach((el, idx) => el.classList.toggle('active', idx === active));
            const el = list.querySelector('.dx-item.active');
            if (el) el.scrollIntoView({ block: 'nearest' });
        }

        function choose(i) {
            const it = shown[i];
            if (!it || (opts.isSelected && opts.isSelected(it.id))) return;
            opts.onSelect(it);
            input.value = '';
            close();
            input.focus();
        }

        input.addEventListener('input', render);
        input.addEventListener('focus', () => { if (input.value.trim()) render(); });
        input.addEventListener('keydown', (e) => {
            if (e.key === 'ArrowDown') { e.preventDefault(); if (list.classList.contains('d-none')) render(); else setActive(active + 1); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(active - 1); }
            else if (e.key === 'Enter') { e.preventDefault(); if (active >= 0) choose(active); }   // Enter tidak boleh mengirim form
            else if (e.key === 'Escape') { close(); }
        });
        // mousedown (bukan click) supaya input tidak kehilangan fokus sebelum pilihan terbaca
        list.addEventListener('mousedown', (e) => {
            const btn = e.target.closest('.dx-item');
            if (!btn) return;
            e.preventDefault();
            choose(Number(btn.dataset.i));
        });
        document.addEventListener('click', (e) => { if (!input.contains(e.target) && !list.contains(e.target)) close(); });
    }

    window.AkpDxPicker = { init };
})();

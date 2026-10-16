/*
 * Penanda nilai pemeriksaan kesehatan di browser.
 * Batas angka TIDAK ditulis di sini - diambil dari window.HEALTH_RANGES (Services/HealthRanges.cs),
 * jadi logika & angka tetap sama dengan yang dipakai server.
 */
window.HealthFlags = (function () {
    'use strict';
    const R = () => window.HEALTH_RANGES;
    const f = (level, label) => ({
        level, label,
        badge: { Normal: 'bg-success', Perhatian: 'bg-warning text-dark', Tinggi: 'bg-danger', Rendah: 'bg-info text-dark' }[level]
    });

    return {
        tekananDarah(sis, dia) {
            if (sis == null || dia == null) return null;
            const t = R().td;
            if (sis >= t.tinggiSis || dia >= t.tinggiDia) return f('Tinggi', 'Tinggi');
            if (sis < t.rendahSis || dia < t.rendahDia) return f('Rendah', 'Rendah');
            if (sis >= t.perhatianSis || dia >= t.perhatianDia) return f('Perhatian', 'Normal tinggi');
            return f('Normal', 'Normal');
        },
        kolesterol(v) {
            if (v == null) return null;
            const k = R().kolesterol;
            if (v >= k.tinggi) return f('Tinggi', 'Tinggi');
            if (v >= k.perhatian) return f('Perhatian', 'Batas tinggi');
            return f('Normal', 'Normal');
        },
        asamUrat(v, jk) {
            if (v == null) return null;
            const a = R().asamUrat;
            const max = jk === 'P' ? a.wanita : a.pria;
            if (v > max) return f('Tinggi', 'Tinggi');
            if (!jk && v > a.wanita) return f('Perhatian', 'Perhatian');
            return f('Normal', 'Normal');
        },
        lingkarPerut(v, jk) {
            if (v == null) return null;
            const l = R().lingkarPerut;
            const max = jk === 'P' ? l.wanita : l.pria;
            if (v >= max) return f('Tinggi', 'Obesitas sentral');
            if (!jk && v >= l.wanita) return f('Perhatian', 'Perhatian');
            return f('Normal', 'Normal');
        },
        gulaDarah(v, jenis) {
            if (v == null) return null;
            const g = R().gula;
            if (v < g.rendah) return f('Rendah', 'Rendah');
            const [p, t] = jenis === 'Puasa' ? [g.puasaPerhatian, g.puasaTinggi] : [g.sewaktuPerhatian, g.sewaktuTinggi];
            if (v >= t) return f('Tinggi', 'Tinggi');
            if (v >= p) return f('Perhatian', 'Batas tinggi');
            return f('Normal', 'Normal');
        },
        imt(v) {
            if (v == null) return null;
            const i = R().imt;
            if (v < i.kurang) return f('Rendah', 'Berat kurang');
            if (v >= i.obesitas) return f('Tinggi', 'Obesitas');
            if (v >= i.lebih) return f('Perhatian', 'Berat lebih');
            return f('Normal', 'Normal');
        }
    };
})();

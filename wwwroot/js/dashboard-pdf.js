/*
 * Export Dashboard -> PDF (A4 portrait), dibuat langsung di browser.
 * - Angka & tabel ditulis sebagai TEKS (tajam, bisa di-copy / di-search di PDF)
 * - Chart diambil dari Chart.js dalam resolusi tinggi (bukan screenshot layar)
 * - Library jsPDF + AutoTable baru dimuat saat tombol diklik, jadi dashboard tetap ringan
 */
(function () {
    'use strict';

    const COMPANY = 'PT. Adhi Kartiko Pratama, Tbk';
    const LIBS = [
        'https://cdn.jsdelivr.net/npm/jspdf@2.5.2/dist/jspdf.umd.min.js',
        'https://cdn.jsdelivr.net/npm/jspdf-autotable@3.8.4/dist/jspdf.plugin.autotable.min.js'
    ];

    // Warna laporan (RGB)
    const C = {
        navy: [31, 56, 100],
        text: [33, 37, 41],
        muted: [108, 117, 125],
        border: [222, 226, 230],
        light: [248, 249, 250],
        fit: [15, 81, 50],
        unfit: [132, 32, 41],
        unfitBg: [248, 215, 218],
        primary: [13, 110, 253]
    };

    const PAGE = { w: 210, h: 297, m: 14 };          // A4 dalam mm, margin 14 mm
    const CONTENT_W = PAGE.w - PAGE.m * 2;
    const fmt = (n) => Number(n || 0).toLocaleString('id-ID');

    // ---------------- util ----------------
    function loadScript(src) {
        return new Promise((resolve, reject) => {
            if (document.querySelector(`script[src="${src}"]`)) return resolve();
            const s = document.createElement('script');
            s.src = src;
            s.onload = resolve;
            s.onerror = () => reject(new Error('Gagal memuat ' + src));
            document.head.appendChild(s);
        });
    }

    async function ensureLibs() {
        if (window.jspdf && window.jspdf.jsPDF && window.jspdf.jsPDF.API.autoTable) return;
        for (const src of LIBS) await loadScript(src);   // berurutan: AutoTable butuh jsPDF dulu
    }

    // Ambil gambar chart dalam resolusi tinggi (scale x lipat), lalu kembalikan ke normal
    function chartImage(chart, scale = 3) {
        const prev = chart.options.devicePixelRatio;
        chart.options.devicePixelRatio = scale;
        chart.resize();
        chart.update('none');
        const url = chart.toBase64Image('image/png', 1);
        const ratio = chart.width / chart.height;
        chart.options.devicePixelRatio = prev;
        chart.resize();
        chart.update('none');
        return { url, ratio };
    }

    // ---------------- bagian-bagian laporan ----------------
    function sectionTitle(doc, y, title, subtitle) {
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(11);
        doc.setTextColor(...C.navy);
        doc.text(title, PAGE.m, y);
        if (subtitle) {
            doc.setFont('helvetica', 'normal');
            doc.setFontSize(8);
            doc.setTextColor(...C.muted);
            doc.text(subtitle, PAGE.m, y + 4.2);
        }
        doc.setDrawColor(...C.navy);
        doc.setLineWidth(0.4);
        doc.line(PAGE.m, y + (subtitle ? 6 : 2), PAGE.m + CONTENT_W, y + (subtitle ? 6 : 2));
        return y + (subtitle ? 10 : 6);
    }

    function header(doc, data, printedAt) {
        doc.setFillColor(...C.navy);
        doc.rect(0, 0, PAGE.w, 24, 'F');
        doc.setTextColor(255, 255, 255);
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(16);
        doc.text('Laporan Dashboard Fit To Work', PAGE.m, 11);
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(9);
        doc.text(COMPANY, PAGE.m, 17.5);
        doc.text(data.today.tanggal, PAGE.w - PAGE.m, 11, { align: 'right' });
        doc.text(`Dicetak: ${printedAt}`, PAGE.w - PAGE.m, 17.5, { align: 'right' });
        return 32;
    }

    function kpiBoxes(doc, y, t) {
        const items = [
            { label: 'Karyawan Aktif', value: fmt(t.totalKaryawan), color: C.text },
            { label: 'Sudah Isi Hari Ini', value: fmt(t.sudahIsi), sub: `${t.persen.toLocaleString('id-ID')}% dari karyawan aktif`, color: C.primary },
            { label: 'Fit & Siap Kerja', value: fmt(t.fit), color: C.fit },
            { label: 'Tidak Fit', value: fmt(t.tidakFit), color: C.unfit },
            { label: 'Belum Isi', value: fmt(t.belumIsi), color: C.muted }
        ];
        const gap = 3;
        const w = (CONTENT_W - gap * (items.length - 1)) / items.length;
        const h = 22;

        items.forEach((it, i) => {
            const x = PAGE.m + i * (w + gap);
            doc.setFillColor(...C.light);
            doc.setDrawColor(...C.border);
            doc.setLineWidth(0.2);
            doc.roundedRect(x, y, w, h, 2, 2, 'FD');
            doc.setFont('helvetica', 'normal');
            doc.setFontSize(7.5);
            doc.setTextColor(...C.muted);
            doc.text(it.label, x + 3, y + 5.5);
            doc.setFont('helvetica', 'bold');
            doc.setFontSize(17);
            doc.setTextColor(...it.color);
            doc.text(it.value, x + 3, y + 14);
            if (it.sub) {
                doc.setFont('helvetica', 'normal');
                doc.setFontSize(6.5);
                doc.setTextColor(...C.muted);
                doc.text(it.sub, x + 3, y + 19);
            }
        });
        return y + h + 8;
    }

    function addChart(doc, y, chart, maxH) {
        const img = chartImage(chart);
        let w = CONTENT_W;
        let h = w / img.ratio;
        if (h > maxH) { h = maxH; w = h * img.ratio; }
        doc.addImage(img.url, 'PNG', PAGE.m + (CONTENT_W - w) / 2, y, w, h, undefined, 'FAST');
        return y + h + 6;
    }

    function coverageBlock(doc, y, cov, chart) {
        const img = chartImage(chart);
        const size = 62;
        const w = size * img.ratio > 90 ? 90 : size * img.ratio;
        const h = w / img.ratio;
        doc.addImage(img.url, 'PNG', PAGE.m, y, w, h, undefined, 'FAST');

        // Keterangan di sebelah kanan chart
        const x = PAGE.m + w + 8;
        const rows = [
            ['Periode', cov.label],
            ['Karyawan aktif', fmt(cov.totalKaryawan)],
            ...(cov.period === 'day' ? [] : [['Hari operasional', fmt(cov.hariOperasional)]]),
            ['Target pengisian', fmt(cov.target)],
            ['Sudah register', fmt(cov.sudahIsi)],
            ['Belum register', fmt(cov.belumIsi)],
            ['Persentase', `${cov.persen.toLocaleString('id-ID')}%`]
        ];
        let ty = y + 8;
        rows.forEach(([k, v], i) => {
            doc.setFont('helvetica', 'normal');
            doc.setFontSize(9);
            doc.setTextColor(...C.muted);
            doc.text(k, x, ty);
            doc.setFont('helvetica', 'bold');
            doc.setTextColor(...(i === rows.length - 1 ? C.fit : C.text));
            doc.text(String(v), x + 42, ty);
            ty += 6.5;
        });
        if (cov.period !== 'day') {
            doc.setFont('helvetica', 'italic');
            doc.setFontSize(7);
            doc.setTextColor(...C.muted);
            doc.text('Target = karyawan aktif x hari yang ada pengisiannya.', x, ty + 1);
        }
        return y + Math.max(h, ty - y) + 6;
    }

    const tableStyle = {
        theme: 'grid',
        margin: { left: PAGE.m, right: PAGE.m },
        styles: { font: 'helvetica', fontSize: 8, cellPadding: 1.8, textColor: C.text, lineColor: C.border, lineWidth: 0.1 },
        headStyles: { fillColor: C.navy, textColor: 255, fontStyle: 'bold' },
        alternateRowStyles: { fillColor: [252, 252, 253] }
    };

    function topTable(doc, y, top) {
        doc.autoTable({
            ...tableStyle,
            startY: y,
            head: [['#', 'Nama', 'NIP', 'Jabatan', 'Section', 'Hari Isi', 'Tidak Fit', 'Terakhir']],
            body: top.length
                ? top.map((t) => [t.rank, t.nama, t.nip, t.jabatan, t.section, t.jumlahHari, t.tidakFit, t.terakhir])
                : [[{ content: 'Belum ada data', colSpan: 8, styles: { halign: 'center', textColor: C.muted } }]],
            columnStyles: {
                0: { halign: 'center', cellWidth: 8 },
                5: { halign: 'right', cellWidth: 15 },
                6: { halign: 'right', cellWidth: 15 },
                7: { halign: 'right', cellWidth: 22 }
            },
            didParseCell: (h) => {
                if (h.section === 'body' && h.column.index === 6 && Number(h.cell.raw) > 0) {
                    h.cell.styles.textColor = C.unfit;
                    h.cell.styles.fontStyle = 'bold';
                }
            }
        });
        return doc.lastAutoTable.finalY + 8;
    }

    function recentTable(doc, y, recent) {
        doc.autoTable({
            ...tableStyle,
            startY: y,
            head: [['Jam', 'Nama', 'NIP', 'Jabatan', 'Section', 'Jadwal', 'Kesiapan']],
            body: recent.length
                ? recent.map((r) => [r.jam, r.nama, r.nip, r.jabatan, r.section, r.jadwalKerja, r.isFit ? 'FIT' : 'TIDAK FIT'])
                : [[{ content: 'Belum ada pengisian hari ini', colSpan: 7, styles: { halign: 'center', textColor: C.muted } }]],
            columnStyles: { 0: { cellWidth: 12 }, 6: { cellWidth: 20, fontStyle: 'bold' } },
            didParseCell: (h) => {
                if (h.section !== 'body' || !recent.length) return;
                const isFit = recent[h.row.index].isFit;
                if (h.column.index === 6) h.cell.styles.textColor = isFit ? C.fit : C.unfit;
                if (!isFit) h.cell.styles.fillColor = C.unfitBg;   // baris TIDAK FIT diberi latar merah muda
            }
        });
        return doc.lastAutoTable.finalY + 8;
    }

    // Pengisian per section (urutan sama dengan tabel di layar)
    const ACH = { hijau: [15, 81, 50], kuning: [102, 77, 3], merah: [132, 32, 41] };
    const ACH_BG = { hijau: [209, 242, 227], kuning: [255, 243, 205], merah: [248, 215, 218] };

    function sectionTable(doc, y, sec, rows) {
        const multiDay = sec.period !== 'day';
        const t = sec.total;
        doc.autoTable({
            ...tableStyle,
            startY: y,
            head: [['Department', 'Section', multiDay ? 'Karyawan (Target)' : 'Karyawan', 'Sudah Isi', 'Belum Isi', 'FIT', 'Tidak Fit', 'Achievement']],
            body: rows.length
                ? rows.map((r) => [r.department, r.section,
                    multiDay ? `${fmt(r.jumlahKaryawan)} (${fmt(r.target)})` : fmt(r.jumlahKaryawan),
                    fmt(r.sudahIsi), fmt(r.belumIsi), fmt(r.fit), fmt(r.tidakFit), r.persen.toLocaleString('id-ID') + '%'])
                : [[{ content: 'Belum ada karyawan aktif', colSpan: 8, styles: { halign: 'center', textColor: C.muted } }]],
            foot: rows.length ? [['', `Total (${rows.length} section)`,
                multiDay ? `${fmt(t.jumlahKaryawan)} (${fmt(t.target)})` : fmt(t.jumlahKaryawan),
                fmt(t.sudahIsi), fmt(t.belumIsi), fmt(t.fit), fmt(t.tidakFit), t.persen.toLocaleString('id-ID') + '%']] : undefined,
            footStyles: { fillColor: C.light, textColor: C.text, fontStyle: 'bold', fontSize: 7.5 },
            columnStyles: {
                2: { halign: 'right' }, 3: { halign: 'right' }, 4: { halign: 'right' },
                5: { halign: 'right' }, 6: { halign: 'right' }, 7: { halign: 'right', fontStyle: 'bold', cellWidth: 22 }
            },
            didParseCell: (h) => {
                if (h.column.index >= 2) h.cell.styles.halign = 'right';
                if (h.section !== 'body' || !rows.length || h.column.index !== 7) return;
                const level = rows[h.row.index].level;
                h.cell.styles.textColor = ACH[level] || C.text;
                h.cell.styles.fillColor = ACH_BG[level] || C.light;
            }
        });
        return doc.lastAutoTable.finalY + 8;
    }

    function footer(doc, printedAt) {
        const pages = doc.getNumberOfPages();
        for (let i = 1; i <= pages; i++) {
            doc.setPage(i);
            doc.setDrawColor(...C.border);
            doc.setLineWidth(0.2);
            doc.line(PAGE.m, PAGE.h - 12, PAGE.w - PAGE.m, PAGE.h - 12);
            doc.setFont('helvetica', 'normal');
            doc.setFontSize(7.5);
            doc.setTextColor(...C.muted);
            doc.text(`Fit To Work · ${COMPANY} · Dicetak ${printedAt}`, PAGE.m, PAGE.h - 7.5);
            doc.text(`Halaman ${i} dari ${pages}`, PAGE.w - PAGE.m, PAGE.h - 7.5, { align: 'right' });
        }
    }

    // Pindah halaman kalau sisa ruang tidak cukup
    function ensureSpace(doc, y, needed) {
        if (y + needed <= PAGE.h - 18) return y;
        doc.addPage();
        return PAGE.m + 4;
    }

    // ---------------- main ----------------
    async function exportPdf(btn) {
        const dash = window.FTW_DASH;
        const data = dash && dash.getData();
        if (!data) {
            toastr.info('Data dashboard belum selesai dimuat, coba lagi sebentar.');
            return;
        }

        btn.disabled = true;
        btn.querySelector('.spinner-border').classList.remove('d-none');
        try {
            await ensureLibs();
            const { jsPDF } = window.jspdf;
            const doc = new jsPDF({ unit: 'mm', format: 'a4', orientation: 'portrait', compress: true });
            const printedAt = dash.siteNowText();
            const days = data.trend.length;

            doc.setProperties({ title: `Laporan Dashboard Fit To Work - ${data.today.tanggal}`, author: COMPANY, creator: 'Fit To Work' });

            // Halaman 1: ringkasan hari ini + tren
            let y = header(doc, data, printedAt);
            y = sectionTitle(doc, y, 'Ringkasan Hari Ini', '1 karyawan dihitung 1x per hari (status terakhir jika isi ulang)');
            y = kpiBoxes(doc, y, data.today);

            y = sectionTitle(doc, y, `Total Register per Hari`, `${days} hari terakhir`);
            y = addChart(doc, y, dash.charts.trend, 72);

            y = ensureSpace(doc, y, 90);
            y = sectionTitle(doc, y, 'Perbandingan Jadwal Kerja', `Non Shift · Shift Pagi · Shift Malam, ${days} hari terakhir`);
            y = addChart(doc, y, dash.charts.shift, 80);

            // Halaman 2: cakupan, Top 10, pengisian terbaru
            y = ensureSpace(doc, y, 80);
            y = sectionTitle(doc, y, 'Karyawan vs Sudah Register', data.coverage.label);
            y = coverageBlock(doc, y, data.coverage, dash.charts.coverage);

            // Pengisian per section (periode mengikuti pilihan Hari/Minggu/Bulan di kartu per section)
            const sec = window.FTW_SECTION && window.FTW_SECTION.getData();
            if (sec) {
                y = ensureSpace(doc, y, 40);
                y = sectionTitle(doc, y, 'Pengisian FTW per Section',
                    `${sec.label} · achievement: hijau >= ${sec.batasHijau}%, kuning >= ${sec.batasKuning}%, merah di bawahnya`);
                y = sectionTable(doc, y, sec, window.FTW_SECTION.getRows());
            }

            y = ensureSpace(doc, y, 40);
            y = sectionTitle(doc, y, 'Top 10 Register Fit To Work', `Paling konsisten mengisi dalam ${days} hari terakhir`);
            y = topTable(doc, y, data.top);

            y = ensureSpace(doc, y, 40);
            y = sectionTitle(doc, y, 'Pengisian Terbaru Hari Ini', `${data.recent.length} pengisian terakhir`);
            recentTable(doc, y, data.recent);

            footer(doc, printedAt);
            doc.save(`Dashboard_FitToWork_${dash.siteNowStamp()}.pdf`);
            toastr.success('PDF dashboard berhasil dibuat.');
        } catch (err) {
            console.error('[FTW] Export PDF gagal', err);
            toastr.error('Gagal membuat PDF: ' + err.message);
        } finally {
            btn.disabled = false;
            btn.querySelector('.spinner-border').classList.add('d-none');
        }
    }

    document.addEventListener('DOMContentLoaded', () => {
        const btn = document.getElementById('btnExportPdf');
        if (btn) btn.addEventListener('click', () => exportPdf(btn));
    });
})();

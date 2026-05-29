import { Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import Chart from 'chart.js/auto';
import * as XLSX from 'xlsx-js-style';
import { DashboardService } from '../services/dashboard.service';
import { AuthService } from '../services/auth.service';

// ── Plugin: etiqueta de valor sobre cada barra ─────────────────────────────────
const BAR_DATALABELS: any = {
  id: 'causasBarLabels',
  afterDatasetsDraw(chart: any) {
    if (chart.config.type !== 'bar') return;
    const { ctx } = chart;
    const isHoriz = chart.options.indexAxis === 'y';
    chart.data.datasets.forEach((ds: any, di: number) => {
      const meta = chart.getDatasetMeta(di);
      if (meta.hidden) return;
      meta.data.forEach((el: any, idx: number) => {
        const val = ds.data[idx];
        if (!val) return;
        const txt = Number(val).toLocaleString('es-MX');
        ctx.save();
        ctx.fillStyle = '#1a202c';
        ctx.font = '600 10px "Segoe UI", sans-serif';
        if (isHoriz) {
          ctx.textAlign = 'left';
          ctx.textBaseline = 'middle';
          ctx.fillText(txt, el.x + 4, el.y);
        } else {
          ctx.textAlign = 'center';
          ctx.textBaseline = 'bottom';
          ctx.fillText(txt, el.x, el.y - 3);
        }
        ctx.restore();
      });
    });
  }
};

// ── Colores para pastel ────────────────────────────────────────────────────────
const PIE_COLORS = [
  'rgb(230, 57, 70)',   'rgb(0, 119, 255)',  'rgb(50, 205, 50)',
  'rgb(255, 200, 0)',   'rgb(128, 0, 128)',  'rgb(255, 102, 0)',
  'rgb(0, 206, 209)',   'rgb(255, 20, 147)', 'rgb(101, 67, 33)',
  'rgb(64, 64, 64)',    'rgb(0, 180, 120)',  'rgb(200, 100, 0)',
];

// ── Plugin: etiquetas dentro del pastel ───────────────────────────────────────
const PIE_DATALABELS: any = {
  id: 'causasPieDatalabels',
  afterDatasetsDraw(chart: any) {
    if (chart.config.type !== 'pie') return;
    const { ctx } = chart;
    const ds     = chart.data.datasets[0];
    const meta   = chart.getDatasetMeta(0);
    const labels = chart.data.labels as string[];
    const total  = (ds.data as number[]).reduce((a: number, b: number) => a + b, 0);

    meta.data.forEach((arc: any, i: number) => {
      const val = ds.data[i] as number;
      if (!val || total === 0 || val / total < 0.04) return;

      const midAngle = arc.startAngle + (arc.endAngle - arc.startAngle) / 2;
      const r  = arc.outerRadius * 0.62;
      const x  = arc.x + Math.cos(midAngle) * r;
      const y  = arc.y + Math.sin(midAngle) * r;
      const pct = ((val / total) * 100).toFixed(1) + '%';

      ctx.save();
      ctx.textAlign    = 'center';
      ctx.textBaseline = 'middle';
      ctx.shadowColor  = 'rgba(0,0,0,0.55)';
      ctx.shadowBlur   = 3;
      ctx.fillStyle    = '#ffffff';
      ctx.font         = 'bold 9px "Segoe UI", sans-serif';
      ctx.fillText(String(labels[i] ?? '').slice(0, 8), x, y - 7);
      ctx.fillText(pct, x, y + 7);
      ctx.restore();
    });
  }
};

@Component({
  selector: 'app-causas',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './causas.component.html',
  styleUrls: ['./causas.component.css']
})
export class CausasComponent implements OnDestroy {
  status: 'WAITING' | 'LOADING' | 'SUCCESS' | 'ERROR' = 'WAITING';
  tableData: any[] = [];
  columns: string[] = [];
  errorMessage = '';

  selectedCode  = 'E02';
  activeCode    = 'E02';
  allCausasData:  { [code: string]: any[] } = {};  // datos año actual (2026)
  prevCausasData: { [code: string]: any[] } = {};  // datos año anterior (2025)
  compareMode = false;

  readonly CODES = [
    { value: 'E02', label: 'E02 — Ramal fuera' },
    { value: 'E03', label: 'E03 — Sector fuera' },
    { value: 'E04', label: 'E04 — Falso contacto distribución' },
    { value: 'E05', label: 'E05 — Improcedente distribución' },
    { value: 'E06', label: 'E06 — Servicio importante fuera' },
    { value: 'E07', label: 'E07 — Reparación mayor' },
    { value: 'Q07', label: 'Q07 — Deficiencia de voltaje' },
  ];

  paretoChart: any = null;
  paretoRows: any[] = [];
  paretoShown = 0;
  paretoTotal = 0;
  paretoPct   = 0;

  chartType: 'bar' | 'line' | 'horizontalBar' | 'pie' = 'bar';

  constructor(
    private dashboardService: DashboardService,
    public auth: AuthService,
    private router: Router
  ) {}

  ngOnDestroy(): void {
    if (this.paretoChart) this.paretoChart.destroy();
  }

  // ── Estado ─────────────────────────────────────────────────────────────────

  get totalRegistros(): number {
    return this.tableData.filter(r => !this.isRowTotal(r)).length;
  }

  isRowTotal(row: any): boolean {
    if (!row) return false;
    return Object.values(row).some(v => /^total$/i.test(String(v ?? '').trim()));
  }

  private parseNum(val: any): number {
    return Number(String(val ?? '0').replace(/[,\s%]/g, '').trim()) || 0;
  }

  // Muestra todas las columnas del scraper en su orden natural, excepto Grafica.
  get orderedColumns(): string[] {
    return this.columns.filter(c => !c.toUpperCase().includes('GRAF'));
  }

  get isAllMode(): boolean {
    return Object.keys(this.allCausasData).length > 0;
  }

  get isCompareMode(): boolean {
    return this.compareMode && Object.keys(this.prevCausasData).length > 0;
  }

  codeRows(code: string): any[] {
    return this.allCausasData[code] || [];
  }

  // ── Comparación año anterior ───────────────────────────────────────────────

  iniciarComparacion(): void {
    // Requiere que los datos de 2026 ya estén cargados
    if (!this.isAllMode) {
      this.status       = 'ERROR';
      this.errorMessage = 'Primero presiona "Consultar" para cargar los datos del año actual.';
      return;
    }

    this.status         = 'LOADING';
    this.prevCausasData = {};
    this.compareMode    = true;

    const prevYear = new Date().getFullYear() - 1;

    this.dashboardService.getCausasAll(prevYear).subscribe({
      next: (data: { [code: string]: any[] }) => {
        this.prevCausasData = data ?? {};
        this.status = 'SUCCESS';
      },
      error: (err: any) => {
        this.status       = 'ERROR';
        this.compareMode  = false;
        const detail = err?.error ?? err?.message ?? '';
        this.errorMessage = detail ? `Error: ${detail}` : 'Error al obtener datos de 2025.';
      }
    });
  }

  getCompareRows(code: string): { Clave: string; Descripcion: string; Anterior: number; Actual: number; Variacion: number }[] {
    const curr: any[] = this.allCausasData[code]  ?? [];
    const prev: any[] = this.prevCausasData[code] ?? [];
    if (!curr.length && !prev.length) return [];

    const both     = [...curr, ...prev];
    const countCol = this.detectCountCol(both);
    const claveCol = this.detectCol(both, 'CLAVE');
    const descCol  = this.detectCol(both, 'DESCRI');

    const currMap  = new Map<string, any>(curr.map((r: any) => [String(r[claveCol] ?? ''), r]));
    const prevMap  = new Map<string, any>(prev.map((r: any) => [String(r[claveCol] ?? ''), r]));
    const allClaves = new Set<string>([
      ...curr.map((r: any) => String(r[claveCol] ?? '')),
      ...prev.map((r: any) => String(r[claveCol] ?? ''))
    ]);

    return Array.from(allClaves)
      .filter(k => k)
      .map(clave => {
        const c        = currMap.get(clave);
        const p        = prevMap.get(clave);
        const currVal  = this.parseNum(c?.[countCol]);
        const prevVal  = this.parseNum(p?.[countCol]);
        const variacion = prevVal > 0
          ? Math.round(((currVal - prevVal) / prevVal) * 10000) / 100
          : 0;
        return {
          Clave:       clave,
          Descripcion: String(c?.[descCol] || p?.[descCol] || ''),
          Anterior:    prevVal,
          Actual:      currVal,
          Variacion:   variacion
        };
      })
      .sort((a, b) => b.Actual - a.Actual);
  }

  private detectCountCol(rows: any[]): string {
    if (!rows.length) return '';
    const cols = Object.keys(rows[0]);
    return cols.find(c => c.toUpperCase().includes('CAUSA') && !c.includes('%'))
        ?? cols.find(c => !c.includes('%') && !/^(Sec|Clave|Descri)/i.test(c.trim()))
        ?? '';
  }

  private detectCol(rows: any[], keyword: string): string {
    if (!rows.length) return keyword;
    return Object.keys(rows[0]).find(c => c.toUpperCase().includes(keyword.toUpperCase())) ?? keyword;
  }

  // ── Detección de columnas ──────────────────────────────────────────────────

  private findCountColumn(): string {
    return (
      this.columns.find(c => c.toUpperCase().includes('CAUSA') && !c.includes('%')) ??
      this.columns.find(c => !c.includes('%') && !/^(SEC|NO\.?|CLAVE)/i.test(c.trim())) ??
      ''
    );
  }

  private findLabelColumn(): string {
    return (
      this.columns.find(c => c.toUpperCase().includes('CLAVE')) ??
      this.columns.find(c => c.toUpperCase().includes('SEC')) ??
      this.columns[0] ?? ''
    );
  }

  private findDescColumn(): string {
    return this.columns.find(c => c.toUpperCase().includes('DESCRI')) ?? '';
  }

  // ── Cálculo Pareto 80/20 ───────────────────────────────────────────────────

  private computePareto(): void {
    const countCol = this.findCountColumn();
    if (!countCol) { this.paretoRows = []; return; }

    const rows = this.tableData
      .filter(r => !this.isRowTotal(r))
      .map(r => ({ ...r, _val: this.parseNum(r[countCol]) }))
      .filter(r => r._val > 0)
      .sort((a, b) => b._val - a._val);

    const total = rows.reduce((s, r) => s + r._val, 0);
    let cumulative = 0;
    const result: any[] = [];

    for (const row of rows) {
      cumulative += row._val;
      result.push(row);
      if (total > 0 && cumulative / total >= 0.80) break;
    }

    this.paretoRows  = result;
    this.paretoShown = result.length;
    this.paretoTotal = rows.length;
    this.paretoPct   = total > 0 ? Math.round((cumulative / total) * 100) : 0;
  }

  // ── Scraping individual ────────────────────────────────────────────────────

  iniciarScraping(code?: string): void {
    if (code) this.selectedCode = code;
    this.status       = 'LOADING';
    this.tableData    = [];
    this.columns      = [];
    this.paretoRows   = [];
    this.allCausasData = {};
    if (this.paretoChart) { this.paretoChart.destroy(); this.paretoChart = null; }

    this.dashboardService.getCausas(this.selectedCode).subscribe({
      next: (data) => {
        this.columns   = data?.length > 0 ? Object.keys(data[0]) : [];
        this.tableData = data ?? [];
        if (data?.length > 0) {
          this.computePareto();
          setTimeout(() => this.buildParetoChart(), 150);
        }
        this.status = 'SUCCESS';
      },
      error: (err) => {
        this.status       = 'ERROR';
        const detail = err?.error ?? err?.message ?? '';
        this.errorMessage = detail ? `Error: ${detail}` : 'Error al obtener los datos. Intenta de nuevo.';
      }
    });
  }

  // ── Scraping todos los códigos de una vez ──────────────────────────────────

  iniciarScrapingAll(): void {
    this.status         = 'LOADING';
    this.tableData      = [];
    this.columns        = [];
    this.paretoRows     = [];
    this.allCausasData  = {};
    this.prevCausasData = {};
    this.compareMode    = false;
    if (this.paretoChart) { this.paretoChart.destroy(); this.paretoChart = null; }

    this.dashboardService.getCausasAll().subscribe({
      next: (data) => {
        this.allCausasData = data ?? {};
        const firstWithData = this.CODES.find(c => (data[c.value] ?? []).length > 0);
        this.switchCode(firstWithData?.value ?? this.CODES[0].value);
        this.status = 'SUCCESS';
      },
      error: (err) => {
        this.status       = 'ERROR';
        const detail = err?.error ?? err?.message ?? '';
        this.errorMessage = detail ? `Error: ${detail}` : 'Error al obtener los datos. Intenta de nuevo.';
      }
    });
  }

  switchCode(code: string): void {
    this.activeCode = code;
    const data = this.allCausasData[code] ?? [];
    this.columns   = data.length > 0 ? Object.keys(data[0]) : [];
    this.tableData = data;
    this.paretoRows = [];
    if (this.paretoChart) { this.paretoChart.destroy(); this.paretoChart = null; }
    if (data.length > 0) {
      this.computePareto();
      setTimeout(() => this.buildParetoChart(), 150);
    }
  }

  // ── Construcción de gráfica ────────────────────────────────────────────────

  onChartTypeChange(type: 'bar' | 'line' | 'horizontalBar' | 'pie'): void {
    this.chartType = type;
    if (this.paretoRows.length > 0) {
      setTimeout(() => this.buildParetoChart(), 50);
    }
  }

  private buildParetoChart(): void {
    if (!this.paretoRows.length) return;

    const countCol = this.findCountColumn();
    const labelCol = this.findLabelColumn();
    const descCol  = this.findDescColumn();

    const labels = this.paretoRows.map(r => String(r[labelCol] ?? '').trim());
    const values = this.paretoRows.map(r => r._val);
    const descs  = this.paretoRows.map(r => String(r[descCol] ?? '').trim());

    if (this.paretoChart) this.paretoChart.destroy();

    const titleText = `Pareto 80/20 — ${this.paretoShown} de ${this.paretoTotal} causas = ${this.paretoPct}% del total`;

    const tooltipTitle = (items: any[]) => {
      const idx = items[0]?.dataIndex;
      return descCol && descs[idx] ? descs[idx] : labels[idx ?? 0];
    };

    // ── PASTEL ────────────────────────────────────────────────────────────────
    if (this.chartType === 'pie') {
      const colors = labels.map((_, i) => PIE_COLORS[i % PIE_COLORS.length]);
      const totalPie = values.reduce((a, b) => a + b, 0);
      this.paretoChart = new Chart('causasChart', {
        type: 'pie',
        data: {
          labels,
          datasets: [{
            data: values,
            backgroundColor: colors,
            borderColor: '#ffffff',
            borderWidth: 2,
            hoverOffset: 8
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            title: { display: true, text: titleText, font: { size: 14, weight: 'bold' }, padding: { bottom: 12 } },
            legend: { position: 'right' as const },
            tooltip: {
              callbacks: {
                label: (ctx: any) => {
                  const pct = totalPie > 0 ? ((ctx.parsed / totalPie) * 100).toFixed(1) : '0.0';
                  const lbl = descs[ctx.dataIndex] || ctx.label;
                  return `  ${lbl}: ${ctx.parsed.toLocaleString()} (${pct}%)`;
                }
              }
            }
          }
        },
        plugins: [PIE_DATALABELS]
      } as any);
      return;
    }

    // ── BARRAS / LÍNEA / BARRAS HORIZONTALES ──────────────────────────────────
    const isHorizontal = this.chartType === 'horizontalBar';
    const isLine       = this.chartType === 'line';
    const resolvedType = isHorizontal ? 'bar' : this.chartType;

    const config: any = {
      type: resolvedType,
      data: {
        labels,
        datasets: [{
          label: countCol || 'Causas',
          data: values,
          backgroundColor: isLine ? 'rgba(59, 130, 246, 0.10)' : 'rgba(59, 130, 246, 0.78)',
          borderColor: 'rgb(37, 99, 235)',
          borderWidth: isLine ? 2.5 : 1,
          tension: 0.35,
          fill: isLine,
          pointRadius:          isLine ? 5 : undefined,
          pointHoverRadius:     isLine ? 8 : undefined,
          pointBackgroundColor: isLine ? 'rgb(37, 99, 235)' : undefined,
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          title: { display: true, text: titleText, font: { size: 14, weight: 'bold' }, padding: { bottom: 12 } },
          legend: { position: 'top' as const },
          tooltip: { callbacks: { title: tooltipTitle } }
        },
        scales: {
          x: { ticks: { maxRotation: 45, minRotation: 0 }, grid: { display: false } },
          y: { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.06)' } }
        }
      },
      plugins: isLine ? [] : [BAR_DATALABELS]
    };

    if (isHorizontal) {
      config.options.indexAxis = 'y';
      config.options.scales = {
        x: { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.06)' } },
        y: { grid: { display: false } }
      };
    }

    this.paretoChart = new Chart('causasChart', config);
  }

  // ── Exportar ───────────────────────────────────────────────────────────────

  exportChart(): void {
    const canvas = document.getElementById('causasChart') as HTMLCanvasElement;
    if (!canvas) return;
    const link   = document.createElement('a');
    link.href     = canvas.toDataURL('image/png');
    link.download = 'pareto_causas.png';
    link.click();
  }

  volver(): void {
    this.router.navigate(['/dashboard']);
  }

  exportExcel(): void {
    const ws = XLSX.utils.json_to_sheet(this.tableData);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Causas');
    XLSX.writeFile(wb, 'causas.xlsx');
  }
}

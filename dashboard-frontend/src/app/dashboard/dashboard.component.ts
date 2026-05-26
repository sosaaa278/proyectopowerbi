import {
  Component,
  OnInit
} from '@angular/core';

import {
  CommonModule
} from '@angular/common';

import {
  FormsModule
} from '@angular/forms';

import {
  HttpClient,
  HttpClientModule
} from '@angular/common/http';

import Chart from 'chart.js/auto';

import * as XLSX from 'xlsx';

import {
  DashboardService
} from '../services/dashboard.service';

@Component({
  selector: 'app-dashboard',

  standalone: true,

  imports: [
    CommonModule,
    FormsModule,
    HttpClientModule
  ],

  templateUrl:
    './dashboard.component.html',

  styleUrls:
    ['./dashboard.component.css']
})

export class DashboardComponent
implements OnInit {

  chart: any;

  compareChart: any;

  compareData: any[] = [];

  chartType: 'bar' | 'line' | 'horizontalBar' = 'bar';

  currentCompareMode: 'all' | 'code' = 'all';

  totalRecords = 0;

  tableDetected = 'NO';

  status = 'WAITING';

  tableData: any[] = [];

  columns: string[] = [];

  allCompareData: {[code: string]: any[]} = {};

  constructor(
    private dashboardService:
      DashboardService
  ) {}

  ngOnInit(): void {

    this.loadCompare();
  }

  // =========================
  // SCRAPING COMPLETO (UN SOLO BROWSER)
  // =========================

  loadData() {

    this.status = 'LOADING';

    this.dashboardService
      .getFullCompare()
      .subscribe({

        next: (data) => {

          this.allCompareData = data.compare;

          if (data.rawData2026.length > 0) {
            this.tableData = data.rawData2026;
            this.columns = Object.keys(data.rawData2026[0]);
          }

          this.totalRecords = data.rawData2026.length;

          this.tableDetected =
            data.rawData2026.length > 0 ? 'YES' : 'NO';

          this.status = 'SUCCESS';

          const codes = Object.keys(data.compare);

          const defaultCode =
            codes.includes('E02') ? 'E02' : codes[0];

          if (defaultCode) {
            this.loadCompareByCode(defaultCode);
          }
        },

        error: () => {
          this.status = 'ERROR';
        }
      });
  }

  // =========================
  // COMPARATIVO TOTAL
  // =========================

  private parseCompareValue(value: any): number {
    return Number(
      String(value)
        .replace(/,/g, '')
        .replace('%', '')
    );
  }

  private buildCompareChartConfig(
    labels: string[],
    values2025: number[],
    values2026: number[],
    useHighlight: boolean
  ) {
    const datasets = [
      {
        label: '2025',
        data: values2025,
        backgroundColor: 'rgba(201, 203, 207, 0.8)',
        borderColor: 'rgba(201, 203, 207, 1)',
        tension: 0.3,
        fill: false
      },
      {
        label: '2026',
        data: values2026,
        backgroundColor: 'rgba(54, 162, 235, 0.8)',
        borderColor: 'rgba(54, 162, 235, 1)',
        tension: 0.3,
        fill: false
      }
    ];

    const chartType =
      this.chartType === 'horizontalBar'
        ? 'bar'
        : this.chartType;

    const config: any = {
      type: chartType,
      data: {
        labels,
        datasets
      },
      options: {
        responsive: true
      }
    };

    if (this.chartType === 'horizontalBar') {
      config.options.indexAxis = 'y';
    }

    if (useHighlight && this.chartType === 'bar') {
      const backgroundPlugin = {
        id: 'customCanvasBackgroundColor',
        beforeDraw: (chart: any) => {
          const {
            ctx,
            chartArea,
            scales
          } = chart;

          const xScale = scales.x;

          ctx.save();

          values2026.forEach((value2026, index) => {
            const value2025 = values2025[index];
            let color = 'rgba(255,255,255,0)';

            if (value2026 > value2025) {
              color = 'rgba(255,0,0,0.12)';
            } else if (value2026 < value2025) {
              color = 'rgba(0,255,0,0.12)';
            }

            const x = xScale.getPixelForValue(index);
            const width = 60;

            ctx.fillStyle = color;
            ctx.fillRect(
              x - width / 2,
              chartArea.top,
              width,
              chartArea.bottom - chartArea.top
            );
          });

          ctx.restore();
        }
      };

      config.plugins = [backgroundPlugin];
    }

    return config;
  }

  private renderCompareChart(
    data: any[],
    useHighlight = false
  ): void {
    this.compareData = data;

    const labels = data.map(x => x.area);
    const values2025 = data.map(x =>
      this.parseCompareValue(x.total2025)
    );
    const values2026 = data.map(x =>
      this.parseCompareValue(x.total2026)
    );

    if (this.compareChart) {
      this.compareChart.destroy();
    }

    this.compareChart = new Chart(
      'compareChart',
      this.buildCompareChartConfig(
        labels,
        values2025,
        values2026,
        useHighlight
      )
    );
  }

  onChartTypeChange(
    chartType: 'bar' | 'line' | 'horizontalBar'
  ): void {
    this.chartType = chartType;

    if (this.compareData.length > 0) {
      this.renderCompareChart(
        this.compareData,
        this.currentCompareMode === 'code'
      );
    }
  }

  loadCompare() {
    this.currentCompareMode = 'all';

    this.dashboardService
      .getCompareData()
      .subscribe(data => {
        this.renderCompareChart(data);
      });
  }

  // =========================
  // COMPARATIVO POR CÓDIGO (DESDE CACHÉ LOCAL)
  // =========================

  loadCompareByCode(code: string) {
    this.currentCompareMode = 'code';

    this.dashboardService
      .getCompareByCode(code)
      .subscribe(data => {
        this.renderCompareChart(data, true);
      });
    const data = this.allCompareData[code];

    if (!data || data.length === 0) return;

    this.compareData = data;

    const labels = data.map(x => x.area);

    const values2025 = data.map(x =>
      Number(String(x.total2025).replace(/,/g, ''))
    );

    const values2026 = data.map(x =>
      Number(String(x.total2026).replace(/,/g, ''))
    );

    if (this.compareChart) {
      this.compareChart.destroy();
    }

    const backgroundPlugin = {

      id: 'customCanvasBackgroundColor',

      beforeDraw: (chart: any) => {

        const { ctx, chartArea, scales } = chart;

        ctx.save();

        values2026.forEach((value2026, index) => {

          const value2025 = values2025[index];

          let color = 'rgba(255,255,255,0)';

          if (value2026 > value2025) {
            color = 'rgba(255,0,0,0.12)';
          } else if (value2026 < value2025) {
            color = 'rgba(0,255,0,0.12)';
          }

          const x = scales.x.getPixelForValue(index);

          ctx.fillStyle = color;

          ctx.fillRect(
            x - 30,
            chartArea.top,
            60,
            chartArea.bottom - chartArea.top
          );
        });

        ctx.restore();
      }
    };

    this.compareChart = new Chart('compareChart', {

      type: 'bar',

      data: {

        labels,

        datasets: [
          {
            label: '2025',
            data: values2025,
            backgroundColor: 'rgba(201, 203, 207, 0.8)'
          },
          {
            label: '2026',
            data: values2026,
            backgroundColor: 'rgba(54, 162, 235, 0.8)'
          }
        ]
      },

      plugins: [backgroundPlugin],

      options: { responsive: true }
    });
  }

  // =========================
  // EXPORTAR EXCEL
  // =========================

  exportExcel(): void {

    const worksheet =
      XLSX.utils.json_to_sheet(
        this.tableData
      );

    const workbook =
      XLSX.utils.book_new();

    XLSX.utils.book_append_sheet(
      workbook,
      worksheet,
      'Dashboard'
    );

    XLSX.writeFile(
      workbook,
      'dashboard.xlsx'
    );
  }

  exportCompareExcel(): void {

    const data = this.compareData.map(item => ({
      'Área': item.area,
      '2025': item.total2025,
      '2026': item.total2026,
      'Variación %': item.variacion
    }));

    const worksheet = XLSX.utils.json_to_sheet(data);

    const workbook = XLSX.utils.book_new();

    XLSX.utils.book_append_sheet(
      workbook,
      worksheet,
      'Comparativo'
    );

    XLSX.writeFile(workbook, 'comparativo_2025_2026.xlsx');
  }

  exportChart(): void {

  const canvas =
    document.getElementById(
      'compareChart'
    ) as HTMLCanvasElement;

  const image =
    canvas.toDataURL(
      'image/png'
    );

  const link =
    document.createElement('a');

  link.href = image;

  link.download =
    'comparativo.png';

  link.click();
}
}
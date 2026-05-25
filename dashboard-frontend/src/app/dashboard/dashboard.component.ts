import {
  Component,
  OnInit
} from '@angular/core';

import {
  CommonModule
} from '@angular/common';

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

  totalRecords = 0;

  tableDetected = 'NO';

  status = 'WAITING';

  tableData: any[] = [];

  columns: string[] = [];

  constructor(
    private http: HttpClient,

    private dashboardService:
      DashboardService
  ) {}

  ngOnInit(): void {

    this.loadCompare();
  }

  // =========================
  // SCRAPING PRINCIPAL
  // =========================

  loadData() {

    this.status = 'LOADING';

    const apiUrl =
      `http://localhost:5111/api/data`;

    this.http.get<any[]>(apiUrl)
      .subscribe(data => {

        this.tableData = data;

        this.totalRecords =
          data.length;

        this.tableDetected =
          data.length > 0
            ? 'YES'
            : 'NO';

        this.status = 'SUCCESS';

        if (data.length > 0) {

          this.columns =
            Object.keys(data[0]);
        }

        const labels =
          data.map(
            (x, index) =>
              `Row ${index + 1}`);

        const values =
          data.map(
            (x, index) =>
              index + 1);

        if (this.chart) {
          this.chart.destroy();
        }

        this.chart =
          new Chart('salesChart', {

            type: 'bar',

            data: {

              labels,

              datasets: [
                {
                  label: 'Rows',
                  data: values
                }
              ]
            }
          });

        // ACTUALIZAR COMPARATIVO
        this.loadCompare();
      });
  }

  // =========================
  // COMPARATIVO TOTAL
  // =========================

  loadCompare() {

    this.dashboardService
      .getCompareData()
      .subscribe(data => {

        this.compareData = data;

        const labels =
          data.map(x => x.area);

        const values2025 =
          data.map(x =>
            Number(
              String(x.total2025)
                .replace(/,/g, '')
                .replace('%', '')
            )
          );

        const values2026 =
          data.map(x =>
            Number(
              String(x.total2026)
                .replace(/,/g, '')
                .replace('%', '')
            )
          );

        if (this.compareChart) {
          this.compareChart.destroy();
        }

        this.compareChart =
          new Chart('compareChart', {

            type: 'bar',

            data: {

              labels,

              datasets: [
                {
                  label: '2025',
                  data: values2025
                },
                {
                  label: '2026',
                  data: values2026
                }
              ]
            },

            options: {
              responsive: true
            }
          });
      });
  }

  // =========================
  // COMPARATIVO POR CÓDIGO
  // =========================

  loadCompareByCode(code: string) {

    this.dashboardService
      .getCompareByCode(code)
      .subscribe(data => {

        const labels =
          data.map(x => x.area);

        const values2025 =
          data.map(x =>
            Number(
              String(x.total2025)
                .replace(/,/g, '')
                .replace('%', '')
            )
          );

        const values2026 =
          data.map(x =>
            Number(
              String(x.total2026)
                .replace(/,/g, '')
                .replace('%', '')
            )
          );

        if (this.compareChart) {
          this.compareChart.destroy();
        }

        const backgroundPlugin = {

  id: 'customCanvasBackgroundColor',

  beforeDraw: (chart: any) => {

    const {
      ctx,
      chartArea,
      scales
    } = chart;

    const xScale =
      scales.x;

    ctx.save();

    values2026.forEach(
      (value2026, index) => {

        const value2025 =
          values2025[index];

        let color =
          'rgba(255,255,255,0)';

        if (value2026 > value2025)
        {
          color =
            'rgba(255,0,0,0.12)';
        }

        else if (
          value2026 < value2025)
        {
          color =
            'rgba(0,255,0,0.12)';
        }

        const x =
          xScale.getPixelForValue(index);

        const width =
          60;

        ctx.fillStyle =
          color;

        ctx.fillRect(
          x - width / 2,
          chartArea.top,
          width,
          chartArea.bottom -
          chartArea.top
        );
      });

    ctx.restore();
  }
};
this.compareChart =
  new Chart('compareChart', {

    type: 'bar',

    data: {

      labels,

      datasets: [

        {
          label: '2025',

          data: values2025,

          backgroundColor:
            'rgba(201, 203, 207, 0.8)'
        },

        {
          label: '2026',

          data: values2026,

          backgroundColor:
            'rgba(54, 162, 235, 0.8)'
        }
      ]
    },

    plugins: [
      backgroundPlugin
    ],

    options: {
      responsive: true
    }
  });
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
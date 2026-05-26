import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Injectable({
  providedIn: 'root'
})
export class DashboardService {

  constructor(
    private http: HttpClient
  ) { }

  getData(url: string) {

    return this.http.get<any[]>(
      `http://localhost:5111/api/data?url=${encodeURIComponent(url)}`
    );
  }

  getCompareByCode(code: string) {

  return this.http.get<any[]>(
    `http://localhost:5111/api/data/compare/${code}`
  );
}

  getCompareData() {

    return this.http.get<any[]>(
      `http://localhost:5111/api/data/compare`
    );
  }

  getFullCompare() {

    return this.http.get<{
      rawData2026: any[];
      compare: {[code: string]: any[]};
    }>(`http://localhost:5111/api/data/fullcompare`);
  }
}
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DashboardComponent } from './dashboard.component';

describe('DashboardComponent', () => {
  let component: DashboardComponent;
  let fixture: ComponentFixture<DashboardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should build a line chart config when the selected chart type is line', () => {
    component.chartType = 'line';

    const config = (component as any).buildCompareChartConfig([
      { area: 'Área A', total2025: 100, total2026: 130 }
    ], false);

    expect(config.type).toBe('line');
    expect(config.data.datasets[0].label).toBe('2025');
    expect(config.data.datasets[1].label).toBe('2026');
  });
});

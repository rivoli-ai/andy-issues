// Copyright (c) Rivoli AI 2026. All rights reserved.

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { RepositoriesComponent } from './repositories.component';

describe('RepositoriesComponent', () => {
  let component: RepositoriesComponent;
  let fixture: ComponentFixture<RepositoriesComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepositoriesComponent, HttpClientTestingModule, RouterTestingModule],
    }).compileComponents();

    fixture = TestBed.createComponent(RepositoriesComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load repositories on init', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne(r => r.url.includes('/api/repositories'));
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('scope')).toBe('mine');

    req.flush({ items: [{ id: '1', name: 'test-repo', provider: 'github', cloneUrl: 'https://github.com/test/repo.git' }], page: 1, pageSize: 20, totalCount: 1 });
    fixture.detectChanges();

    expect(component.repos.length).toBe(1);
    expect(component.repos[0].name).toBe('test-repo');
  });

  it('should filter repositories by search', () => {
    component.repos = [
      { id: '1', name: 'frontend', provider: 'github', cloneUrl: 'https://github.com/a/frontend.git' } as any,
      { id: '2', name: 'backend', provider: 'github', cloneUrl: 'https://github.com/a/backend.git' } as any,
    ];
    component.search = 'front';
    expect(component.filteredRepos.length).toBe(1);
    expect(component.filteredRepos[0].name).toBe('frontend');
  });

  it('should calculate total pages', () => {
    component.totalCount = 45;
    component.pageSize = 20;
    expect(component.totalPages).toBe(3);
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, UrlTree } from '@angular/router';
import { firstValueFrom, isObservable, Observable, of } from 'rxjs';
import { permissionGuard } from './permission.guard';

describe('permissionGuard()', () => {
  let http: HttpTestingController;
  const router = {
    createUrlTree: jasmine.createSpy('createUrlTree').and.returnValue({} as UrlTree),
  };

  beforeEach(() => {
    router.createUrlTree.calls.reset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function run(policy: string): Promise<boolean | UrlTree> {
    const result = TestBed.runInInjectionContext(() =>
      permissionGuard(policy)({} as any, {} as any),
    );
    return isObservable(result) ? await firstValueFrom(result as Observable<boolean | UrlTree>) : await Promise.resolve(result as boolean | UrlTree);
  }

  it('allows when API returns the policy', async () => {
    const promise = run('Tickets.ViewAll');
    http.expectOne('/api/me/permissions').flush({ permissions: ['Tickets.ViewAll'] });
    expect(await promise).toBeTrue();
  });

  it('redirects when policy missing', async () => {
    const promise = run('Audit.ViewActivity');
    http.expectOne('/api/me/permissions').flush({ permissions: [] });
    await promise;
    expect(router.createUrlTree).toHaveBeenCalledWith(['/login']);
  });
});

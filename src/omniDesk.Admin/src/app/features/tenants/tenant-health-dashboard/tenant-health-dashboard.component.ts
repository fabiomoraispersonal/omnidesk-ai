import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { interval, Subscription, switchMap, startWith } from 'rxjs';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TenantService } from '../services/tenant.service';
import { TenantMetricsDetail } from '../models/tenant.models';

@Component({
  selector: 'app-tenant-health-dashboard',
  standalone: true,
  imports: [CommonModule, CardModule, TagModule, ButtonModule, ProgressSpinnerModule],
  template: `
    <div class="flex items-center justify-between mb-4">
      <h2 class="text-xl font-semibold">Dashboard de Saúde</h2>
      <p-button label="Voltar" severity="secondary" (onClick)="router.navigate(['..'], { relativeTo: route })" />
    </div>

    @if (loading()) {
      <p-progressSpinner />
    } @else if (metrics()) {
      <div class="grid grid-cols-2 gap-4">
        <p-card header="PostgreSQL">
          <div class="flex items-center gap-2">
            <span [class]="metrics()!.postgres.connected ? 'text-green-600 text-2xl' : 'text-red-600 text-2xl'">
              {{ metrics()!.postgres.connected ? '✅' : '❌' }}
            </span>
            <span>{{ metrics()!.postgres.connected ? 'Conectado' : metrics()!.postgres.error }}</span>
          </div>
          @if (metrics()!.postgres_schema_size_mb !== undefined) {
            <p class="mt-2 text-sm text-gray-600">Tamanho: {{ metrics()!.postgres_schema_size_mb }} MB</p>
          }
        </p-card>

        <p-card header="Redis">
          <div class="flex items-center gap-2">
            <span [class]="metrics()!.redis.connected ? 'text-green-600 text-2xl' : 'text-red-600 text-2xl'">
              {{ metrics()!.redis.connected ? '✅' : '❌' }}
            </span>
            <span>{{ metrics()!.redis.connected ? 'Conectado' : metrics()!.redis.error }}</span>
          </div>
          @if (metrics()!.redis_keys !== undefined) {
            <p class="mt-2 text-sm text-gray-600">Chaves: {{ metrics()!.redis_keys }}</p>
          }
        </p-card>

        <p-card header="MongoDB">
          <div class="flex items-center gap-2">
            <span [class]="metrics()!.mongodb.connected ? 'text-green-600 text-2xl' : 'text-red-600 text-2xl'">
              {{ metrics()!.mongodb.connected ? '✅' : '❌' }}
            </span>
            <span>{{ metrics()!.mongodb.connected ? 'Conectado' : metrics()!.mongodb.error }}</span>
          </div>
          @if (metrics()!.mongodb_size_mb !== undefined) {
            <p class="mt-2 text-sm text-gray-600">Tamanho: {{ metrics()!.mongodb_size_mb }} MB &nbsp;·&nbsp; Documentos: {{ metrics()!.mongodb_documents }}</p>
          }
        </p-card>

        <p-card header="MinIO">
          @if (metrics()!.minio_objects !== undefined) {
            <p class="text-sm text-gray-600">Objetos: {{ metrics()!.minio_objects }}</p>
            <p class="text-sm text-gray-600">Tamanho: {{ metrics()!.minio_size_mb }} MB</p>
          } @else {
            <p class="text-sm text-gray-500">Dados indisponíveis</p>
          }
        </p-card>
      </div>

      <div class="grid grid-cols-3 gap-4 mt-4">
        <p-card header="Conversas (30d)">
          <p class="text-3xl font-bold">{{ metrics()!.conversations_last_30d }}</p>
          @if (metrics()!.conversations_by_channel) {
            <p class="text-sm text-gray-500 mt-1">
              WhatsApp: {{ metrics()!.conversations_by_channel.whatsapp }} &nbsp;·&nbsp;
              Live Chat: {{ metrics()!.conversations_by_channel.live_chat }}
            </p>
          }
        </p-card>
        <p-card header="Tickets Abertos">
          <p class="text-3xl font-bold">{{ metrics()!.open_tickets }}</p>
        </p-card>
        <p-card header="Usuários Ativos">
          <p class="text-3xl font-bold">{{ metrics()!.active_users }}</p>
        </p-card>
      </div>

      <p class="text-sm text-gray-400 mt-4">Atualiza automaticamente a cada 60 segundos.</p>
    } @else {
      <p class="text-gray-500">Métricas ainda não disponíveis. Aguarde até 5 minutos após o provisionamento.</p>
    }
  `,
})
export class TenantHealthDashboardComponent implements OnInit, OnDestroy {
  protected readonly router = inject(Router);
  protected readonly route = inject(ActivatedRoute);
  private readonly tenantService = inject(TenantService);

  protected readonly metrics = signal<TenantMetricsDetail | null>(null);
  protected readonly loading = signal(true);

  private sub?: Subscription;
  private tenantId = '';

  ngOnInit(): void {
    this.tenantId = this.route.snapshot.paramMap.get('id')!;

    this.sub = interval(60_000).pipe(
      startWith(0),
      switchMap(() => this.tenantService.getTenantMetrics(this.tenantId)),
    ).subscribe({
      next: (data) => { this.metrics.set(data); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }
}

import { ChangeDetectionStrategy, Component, OnInit, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ApiKeysService, ApiKeyResponse } from './api-keys.service';
import { CreateApiKeyDialogComponent } from './create-api-key-dialog/create-api-key-dialog.component';

@Component({
  selector: 'app-api-keys',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, TableModule, ButtonModule, TagModule, ToastModule,
    ConfirmDialogModule, CreateApiKeyDialogComponent,
  ],
  providers: [ConfirmationService, MessageService],
  template: `
    <p-toast />
    <p-confirmDialog />
    <app-create-api-key-dialog #createDialog (created)="refresh()" />

    <div class="api-keys-page">
      <div class="api-keys-page__header">
        <div>
          <h1>Chaves de API</h1>
          <p>Autentique ferramentas externas (ex: Metabase) via header <code>X-Api-Key</code>.</p>
        </div>
        <p-button
          label="Nova chave"
          icon="pi pi-plus"
          (click)="createDialog.open()"
          [disabled]="(keys().length >= 5)" />
      </div>

      @if (keys().length >= 5) {
        <p class="limit-warn">Limite de 5 chaves ativas atingido. Revogue uma chave para criar outra.</p>
      }

      <p-table
        [value]="keys()"
        [loading]="loading()"
        styleClass="p-datatable-sm">

        <ng-template pTemplate="header">
          <tr>
            <th>Nome</th>
            <th>Escopos</th>
            <th>Último uso</th>
            <th>Status</th>
            <th>Criada em</th>
            <th style="width:6rem"></th>
          </tr>
        </ng-template>

        <ng-template pTemplate="body" let-key>
          <tr>
            <td>{{ key.name }}</td>
            <td>{{ key.scopes.join(', ') }}</td>
            <td>{{ key.last_used_at ? (key.last_used_at | date:'dd/MM/yyyy HH:mm') : '—' }}</td>
            <td>
              <p-tag
                [severity]="key.revoked ? 'danger' : 'success'"
                [value]="key.revoked ? 'Revogada' : 'Ativa'" />
            </td>
            <td>{{ key.created_at | date:'dd/MM/yyyy' }}</td>
            <td>
              @if (!key.revoked) {
                <p-button
                  label="Revogar"
                  severity="danger"
                  size="small"
                  [text]="true"
                  (click)="confirmRevoke(key)" />
              }
            </td>
          </tr>
        </ng-template>

        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="6" style="text-align:center;padding:2rem;color:var(--color-text-muted)">
              Nenhuma chave criada ainda.
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>
  `,
  styles: [`
    .api-keys-page { padding: 1.5rem; }
    .api-keys-page__header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 1.5rem; }
    .api-keys-page__header h1 { margin: 0 0 0.25rem; font-size: 1.5rem; }
    .api-keys-page__header p { margin: 0; color: var(--color-text-muted); font-size: 0.875rem; }
    .api-keys-page__header code { background: var(--surface-100); padding: 0.1rem 0.3rem; border-radius: 3px; font-size: 0.8rem; }
    .limit-warn { color: var(--color-warning); font-size: 0.875rem; margin-bottom: 1rem; }
  `],
})
export class ApiKeysComponent implements OnInit {
  @ViewChild('createDialog') createDialog!: CreateApiKeyDialogComponent;

  private readonly service = inject(ApiKeysService);
  private readonly confirm = inject(ConfirmationService);
  private readonly toast = inject(MessageService);

  keys    = signal<ApiKeyResponse[]>([]);
  loading = signal(false);

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.service.listApiKeys().subscribe({
      next: data => {
        this.keys.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao carregar chaves.' });
      },
    });
  }

  confirmRevoke(key: ApiKeyResponse): void {
    this.confirm.confirm({
      message: `Revogar a chave "${key.name}"? Esta ação não pode ser desfeita.`,
      header: 'Revogar chave',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Revogar',
      rejectLabel: 'Cancelar',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => this.revoke(key.id),
    });
  }

  private revoke(id: string): void {
    this.service.revokeApiKey(id).subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'Revogada', detail: 'Chave revogada com sucesso.' });
        this.refresh();
      },
      error: () => {
        this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao revogar chave.' });
      },
    });
  }
}

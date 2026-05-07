using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AgentTemplates;

namespace omniDesk.Api.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await db.AgentTemplates.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.AgentTemplates.AddRange(
                new AgentTemplate
                {
                    Name = "Agente Principal",
                    Type = AgentType.Orchestrator,
                    Description = "Ponto de entrada. Faz saudação, qualifica o cliente e decide qual agente acionar.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new AgentTemplate
                {
                    Name = "Recepção",
                    Type = AgentType.SubAgent,
                    Description = "Responsável por informações gerais, localização, horários de funcionamento e primeiro contato.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new AgentTemplate
                {
                    Name = "Vendas",
                    Type = AgentType.SubAgent,
                    Description = "Responsável por apresentar procedimentos/serviços, passar valores iniciais e conduzir o lead ao agendamento de avaliação.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new AgentTemplate
                {
                    Name = "Pós-Vendas",
                    Type = AgentType.SubAgent,
                    Description = "Responsável por clientes que já realizaram procedimentos: dúvidas, retornos e satisfação.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new AgentTemplate
                {
                    Name = "Suporte",
                    Type = AgentType.SubAgent,
                    Description = "Responsável por problemas, reclamações e situações que exigem atenção especial.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                }
            );

            await db.SaveChangesAsync();
        }
    }
}

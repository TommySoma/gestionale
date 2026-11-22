using System.Security.Claims;
using Gestionale.Domain.Sicurezza;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Gestionale.Api.Autenticazione;

/// <summary>L'utente deve avere il permesso indicato, con qualsiasi ambito.</summary>
internal sealed record RequisitoPermesso(string Codice) : IAuthorizationRequirement;

internal sealed class GestoreRequisitoPermesso : AuthorizationHandler<RequisitoPermesso>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RequisitoPermesso requirement)
    {
        if (context.User.AmbitoPermesso(requirement.Codice) is not null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Crea al volo le policy <c>permesso:codice</c>, senza registrarle una per una.</summary>
internal sealed class FornitorePolicyPermessi(IOptions<AuthorizationOptions> opzioni) : DefaultAuthorizationPolicyProvider(opzioni)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        if (policyName.StartsWith(PolicyAutenticazione.PrefissoPermesso, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new RequisitoPermesso(policyName[PolicyAutenticazione.PrefissoPermesso.Length..]))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

internal static class EstensioniPermessi
{
    /// <summary>L'endpoint richiede il permesso indicato; l'ambito "propri" lo filtra l'endpoint stesso.</summary>
    public static TBuilder RichiedePermesso<TBuilder>(this TBuilder builder, string codice)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PolicyAutenticazione.PrefissoPermesso + codice);

    /// <summary>Ambito del permesso per l'utente, oppure null se non lo ha.</summary>
    public static AmbitoPermesso? AmbitoPermesso(this ClaimsPrincipal utente, string codice)
    {
        AmbitoPermesso? ambito = null;
        foreach (var claim in utente.FindAll(ClaimGestionale.Permesso))
        {
            if (claim.Value == codice)
            {
                return Domain.Sicurezza.AmbitoPermesso.Tutti;
            }

            if (claim.Value == codice + ClaimGestionale.SuffissoPropri)
            {
                ambito = Domain.Sicurezza.AmbitoPermesso.Propri;
            }
        }

        return ambito;
    }

    public static Guid UtenteId(this ClaimsPrincipal utente) =>
        Guid.Parse(utente.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Claim sub mancante."));

    public static Guid AziendaId(this ClaimsPrincipal utente) =>
        Guid.Parse(utente.FindFirstValue(ClaimGestionale.Azienda)
            ?? throw new InvalidOperationException("Claim azienda mancante."));

    public static string? Scopo(this ClaimsPrincipal utente) => utente.FindFirstValue(ClaimGestionale.Scopo);
}

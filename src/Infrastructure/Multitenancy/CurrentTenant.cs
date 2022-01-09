using DN.WebApi.Application.Common.Caching;
using DN.WebApi.Application.Multitenancy;
using DN.WebApi.Infrastructure.Persistence;
using DN.WebApi.Infrastructure.Persistence.Contexts;
using DN.WebApi.Shared.Multitenancy;
using Mapster;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace DN.WebApi.Infrastructure.Multitenancy;

public class CurrentTenant : ICurrentTenant, ICurrentTenantInitializer
{
    private readonly DatabaseSettings _options;
    private readonly IStringLocalizer<CurrentTenant> _localizer;
    private readonly TenantManagementDbContext _context;
    private readonly ICacheService _cache;

    private TenantDto? _currentTenant;

    public CurrentTenant(
        IOptions<DatabaseSettings> options,
        IStringLocalizer<CurrentTenant> localizer,
        TenantManagementDbContext context,
        ICacheService cache)
    {
        _options = options.Value;
        _localizer = localizer;
        _context = context;
        _cache = cache;
    }

    public TenantDto Tenant => _currentTenant ?? throw new InvalidOperationException("Current Tenant not set.");
    public string Key => Tenant.Key ?? throw new InvalidOperationException("Current Tenant Key not set.");
    public string DbProvider => _options.DBProvider ?? throw new InvalidOperationException("Current Tenant DbProvider not set.");

    public bool TryGetKey([NotNullWhen(true)] out string? tenantKey)
    {
        if (_currentTenant?.Key is string key)
        {
            tenantKey = key;
            return true;
        }

        tenantKey = null;
        return false;
    }

    public bool TryGetConnectionString([NotNullWhen(true)] out string? tennantConnectionString)
    {
        if (_currentTenant?.ConnectionString is string connecctionString)
        {
            tennantConnectionString = connecctionString;
            return true;
        }

        tennantConnectionString = null;
        return false;
    }

    public void SetCurrentTenant(string tenantKey)
    {
        if (_currentTenant is not null)
        {
            throw new Exception("Method reserved for in-scope initialization");
        }

        var tenantDto = _cache.GetOrSet(
            CacheKeys.GetCacheKey("tenant", tenantKey),
            () => _context.Tenants
                .Where(a => a.Key == tenantKey)
                .FirstOrDefault()?
                .Adapt<TenantDto>());

        if (tenantDto is null)
        {
            throw new InvalidTenantException(_localizer["tenant.invalid"]);
        }

        if (tenantDto.Key != MultitenancyConstants.Root.Key)
        {
            if (!tenantDto.IsActive)
            {
                throw new InvalidTenantException(_localizer["tenant.inactive"]);
            }

            if (DateTime.UtcNow > tenantDto.ValidUpto)
            {
                throw new InvalidTenantException(_localizer["tenant.expired"]);
            }
        }

        _currentTenant = tenantDto;
        if (string.IsNullOrEmpty(_currentTenant.ConnectionString))
        {
            _currentTenant.ConnectionString = _options.ConnectionString;
        }
    }
}
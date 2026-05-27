using System;
using System.Threading.Tasks;
using FluxoDeCaixa.Core.Entities;
using FluxoDeCaixa.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace FluxoDeCaixa.Infrastructure.Cache
{
    public class InMemoryConsolidadoCache : IConsolidadoCache
    {
        private readonly IMemoryCache _memoryCache;

        public InMemoryConsolidadoCache(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        public Task<ConsolidadoDiario?> ObterAsync(DateTime data, string conta)
        {
            var cacheKey = BuildCacheKey(data, conta);
            _memoryCache.TryGetValue(cacheKey, out ConsolidadoDiario? consolidado);
            return Task.FromResult(consolidado);
        }

        public Task SalvarAsync(ConsolidadoDiario consolidado)
        {
            var cacheKey = BuildCacheKey(consolidado.Data, consolidado.Conta);
            _memoryCache.Set(cacheKey, consolidado, TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(DateTime data, string conta)
        {
            var cacheKey = BuildCacheKey(data, conta);
            _memoryCache.Remove(cacheKey);
            return Task.CompletedTask;
        }

        private static string BuildCacheKey(DateTime data, string conta)
        {
            return $"consolidado:{data:yyyy-MM-dd}:{conta}";
        }
    }
}

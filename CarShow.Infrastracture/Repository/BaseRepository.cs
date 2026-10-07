using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Infrastracture.Repository
{
    public class BaseRepository<TKey,TEntity> : IBaseRepository<TKey, TEntity>
        where TEntity : BaseEntity<TKey>
        where TKey : struct
    {
        protected readonly CarShowContext _context;
        protected readonly DbSet<TEntity> _dbSet;

        public BaseRepository(CarShowContext context)
        {
            _context = context;
            _dbSet = _context.Set<TEntity>();
        }

        public async Task Create(TEntity entity)
        {
            await _dbSet.AddAsync(entity);
        }

        public Task Delete(TEntity entity)
        {
            _dbSet.Remove(entity);
            return Task.CompletedTask;
        }

        public IQueryable<TEntity> GetAll()
        {
            return _dbSet.AsNoTracking();
        }

        public async Task<TEntity> GetById(TKey key)
        {
            return await _dbSet.FindAsync(key);
        }

        public async Task SaveChanges()
        {
            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {
                // A failed entity must not poison the scoped DbContext for the next crawler row.
                _context.ChangeTracker.Clear();
                throw;
            }
        }

        public Task Update(TEntity entity)
        {
            _dbSet.Update(entity);
            return Task.CompletedTask;
        }
    }
}
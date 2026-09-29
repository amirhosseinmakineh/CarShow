using CarShow.Domain.Models;

namespace CarShow.Domain.IRepository
{
    public interface IBaseRepository<TKey, TEntity> where TEntity : BaseEntity<TKey> where TKey : struct
    {
        IQueryable<TEntity> GetAll();
        Task Create(TEntity entity);
        Task Update(TEntity entity);
        Task Delete(TEntity entity);
        Task<TEntity> GetById(TKey key);
        Task SaveChanges();
    }
}

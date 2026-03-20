namespace TTA.DataAccess.Models.Base;

public interface IKeyedEntity<TKey> : IKeyedEntity
{
    TKey Id { get; set; }
}

public interface IKeyedEntity
{
}
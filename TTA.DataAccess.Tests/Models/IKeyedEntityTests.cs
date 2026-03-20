using TTA.DataAccess.Models;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Tests.Models;

public class IKeyedEntityTests
{
    [Fact]
    public void IKeyedEntityGeneric_ExtendsIKeyedEntity_MarkerInterface()
    {
        // IKeyedEntity<TKey> inherits from the non-generic IKeyedEntity
        var cityType = typeof(City);
        Assert.True(typeof(IKeyedEntity).IsAssignableFrom(cityType));
        Assert.True(typeof(IKeyedEntity<Guid>).IsAssignableFrom(cityType));
    }

    [Fact]
    public void IKeyedEntity_IsMarkerInterface_HasNoMethods()
    {
        var methods = typeof(IKeyedEntity).GetMethods();
        Assert.Empty(methods);
    }

    [Fact]
    public void IKeyedEntityGeneric_HasIdProperty()
    {
        var idProperty = typeof(IKeyedEntity<Guid>).GetProperty("Id");
        Assert.NotNull(idProperty);
    }

    [Fact]
    public void IKeyedEntityGeneric_IdProperty_HasGetter()
    {
        var idProperty = typeof(IKeyedEntity<Guid>).GetProperty("Id");
        Assert.NotNull(idProperty!.GetMethod);
    }

    [Fact]
    public void IKeyedEntityGeneric_IdProperty_HasSetter()
    {
        var idProperty = typeof(IKeyedEntity<Guid>).GetProperty("Id");
        Assert.NotNull(idProperty!.SetMethod);
    }

    [Theory]
    [InlineData(typeof(City))]
    [InlineData(typeof(Club))]
    [InlineData(typeof(EventDefinition))]
    [InlineData(typeof(GameEvent))]
    [InlineData(typeof(Match))]
    [InlineData(typeof(MatchLineup))]
    [InlineData(typeof(Player))]
    [InlineData(typeof(PlayerMetric))]
    [InlineData(typeof(PlayerPositionDefinition))]
    [InlineData(typeof(PlayerPresence))]
    [InlineData(typeof(PlayerRoster))]
    [InlineData(typeof(Sport))]
    [InlineData(typeof(SportConfiguration))]
    [InlineData(typeof(Team))]
    [InlineData(typeof(TeamMembership))]
    [InlineData(typeof(TimeAnchor))]
    [InlineData(typeof(Tournament))]
    public void AllGuidKeyedModels_ImplementIKeyedEntityOfGuid(Type modelType)
    {
        Assert.True(typeof(IKeyedEntity<Guid>).IsAssignableFrom(modelType),
            $"{modelType.Name} should implement IKeyedEntity<Guid>");
    }

    [Fact]
    public void Region_ImplementsIKeyedEntityOfInt()
    {
        Assert.True(typeof(IKeyedEntity<int>).IsAssignableFrom(typeof(Region)));
    }

    [Fact]
    public void User_ImplementsIKeyedEntityOfString()
    {
        Assert.True(typeof(IKeyedEntity<string>).IsAssignableFrom(typeof(User)));
    }

    [Theory]
    [InlineData(typeof(City))]
    [InlineData(typeof(Club))]
    [InlineData(typeof(Region))]
    [InlineData(typeof(User))]
    [InlineData(typeof(Player))]
    [InlineData(typeof(Team))]
    [InlineData(typeof(Match))]
    [InlineData(typeof(Tournament))]
    public void AllModels_ImplementNonGenericIKeyedEntity(Type modelType)
    {
        Assert.True(typeof(IKeyedEntity).IsAssignableFrom(modelType),
            $"{modelType.Name} should implement non-generic IKeyedEntity");
    }

    [Fact]
    public void IKeyedEntityGeneric_CanBeUsedPolymorphically_WithGuidId()
    {
        var id = Guid.NewGuid();
        IKeyedEntity<Guid> entity = new City { Id = id };
        Assert.Equal(id, entity.Id);

        var newId = Guid.NewGuid();
        entity.Id = newId;
        Assert.Equal(newId, entity.Id);
    }

    [Fact]
    public void IKeyedEntity_CanBeUsedPolymorphically_AsMarker()
    {
        IKeyedEntity entity = new Region { Id = 1, Name = "Test" };
        Assert.NotNull(entity);
    }
}
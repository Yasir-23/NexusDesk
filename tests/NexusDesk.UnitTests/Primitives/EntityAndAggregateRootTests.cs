using FluentAssertions;
using NexusDesk.SharedKernel.Primitives;
using Xunit;

namespace NexusDesk.UnitTests.Primitives;

public class TestEntity : Entity<Guid>
{
    public string Name { get; set; }

    public TestEntity(Guid id, string name) : base(id)
    {
        Name = name;
    }
}

public record TestDomainEvent : DomainEvent
{
    public string EventData { get; init; } = string.Empty;
}

public class TestAggregateRoot : AggregateRoot<Guid>
{
    public TestAggregateRoot(Guid id) : base(id) { }

    public void DoSomething()
    {
        AddDomainEvent(new TestDomainEvent { EventData = "EventHappened" });
    }
}

public class EntityTests
{
    [Fact]
    public void TwoEntities_WithSameIdAndType_ShouldBeEqual()
    {
        var id = Guid.NewGuid();
        var entity1 = new TestEntity(id, "Alpha");
        var entity2 = new TestEntity(id, "Beta");

        entity1.Should().Be(entity2);
        (entity1 == entity2).Should().BeTrue();
        (entity1 != entity2).Should().BeFalse();
        entity1.GetHashCode().Should().Be(entity2.GetHashCode());
    }

    [Fact]
    public void TwoEntities_WithDifferentIds_ShouldNotBeEqual()
    {
        var entity1 = new TestEntity(Guid.NewGuid(), "Alpha");
        var entity2 = new TestEntity(Guid.NewGuid(), "Alpha");

        entity1.Should().NotBe(entity2);
        (entity1 == entity2).Should().BeFalse();
        (entity1 != entity2).Should().BeTrue();
    }

    [Fact]
    public void Entity_ComparedToNull_ShouldNotBeEqual()
    {
        var entity = new TestEntity(Guid.NewGuid(), "Alpha");

        entity.Equals(null).Should().BeFalse();
        (entity == null).Should().BeFalse();
        (null == entity).Should().BeFalse();
    }
}

public class AggregateRootTests
{
    [Fact]
    public void AddDomainEvent_ShouldEnqueueEvent()
    {
        var root = new TestAggregateRoot(Guid.NewGuid());
        root.DomainEvents.Should().BeEmpty();

        root.DoSomething();

        root.DomainEvents.Should().HaveCount(1);
        root.DomainEvents.First().Should().BeOfType<TestDomainEvent>();
    }

    [Fact]
    public void ClearDomainEvents_ShouldEmptyQueue()
    {
        var root = new TestAggregateRoot(Guid.NewGuid());
        root.DoSomething();
        root.DoSomething();
        root.DomainEvents.Should().HaveCount(2);

        root.ClearDomainEvents();

        root.DomainEvents.Should().BeEmpty();
    }
}

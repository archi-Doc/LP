﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1124 // Do not use regions

namespace CrystalData;

/// <summary>
/// A sample data class.<br/>
/// This is the idea that each data are arranged in the ordered structure and constitute a single crystal.
/// </summary>
[TinyhandObject(ExplicitKeyOnly = true)]
[ValueLinkObject]
internal partial class SampleData : BaseData
{
    public SampleData(ICrystalInternal crystal, BaseData? parent, string name)
        : base(crystal, parent)
    {
        this.name = name;
    }

    public SampleData()
    {
    }

    [Key(3)]
    [Link(Primary = true, Type = ChainType.Unordered)]
    private string name = string.Empty;

    [Key(4)]
    private GoshujinClass? children;

    [Key(5)]
    [Link(Type = ChainType.Ordered)]
    private int age;

    #region Child

    public SampleData GetOrCreateChild(string name)
    {
        SampleData? data;
        using (this.semaphore.Lock())
        {
            this.children ??= new();
            if (!this.children.NameChain.TryGetValue(name, out data))
            {
                data = new SampleData(this.Crystal, this, name);
                this.children.Add(data);
            }

            data.NameValue = "test";
        }

        return data;
    }

    public SampleData? TryGetChild(string name)
    {
        SampleData? data;
        using (this.semaphore.Lock())
        {
            if (this.children == null)
            {
                return null;
            }

            this.children.NameChain.TryGetValue(name, out data);
            return data;
        }
    }

    public bool DeleteChild(string name)
    {
        using (this.semaphore.Lock())
        {
            if (this.children == null)
            {
                return false;
            }

            if (this.children.NameChain.TryGetValue(name, out var data))
            {
                data.DeleteActual();
                return true;
            }
        }

        return false;
    }

    #endregion

    protected override IEnumerator<BaseData> EnumerateInternal()
    {
        if (this.children == null)
        {
            yield break;
        }

        foreach (var x in this.children)
        {
            yield return x;
        }
    }

    protected override void DeleteInternal()
    {
        this.children = null;
        this.Goshujin = null;
    }
}
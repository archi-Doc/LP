## CrystalData is a storage engine for C#

- Very versatile and easy to use.
- Covers a wide range of storage needs.

- Full serialization features integrated with [Tinyhand](https://github.com/archi-Doc/Tinyhand).



## Table of Contents

- [Requirements](#requirements)
- [Quick Start](#quick-start)



## Requirements

**Visual Studio 2022** or later for Source Generator V2.

**C# 11** or later for generated codes.

**.NET 7** or later target framework.



## Quick start

Install CrystalData using Package Manager Console.

```
Install-Package CrystalData
```

This is a small example code to use CrystalData.

```csharp
// First, create a class to represent the data content.
[TinyhandObject] // Annotate TinyhandObject attribute to make this class serializable.
public partial class FirstData
{
    [Key(0)] // The key attribute specifies the index at serialization
    public int Id { get; set; }

    [Key(1)]
    [DefaultValue("Hoge")] // The default value for the name property.
    public string Name { get; set; } = string.Empty;

    public override string ToString()
        => $"Id: {this.Id}, Name: {this.Name}";
}
```

```csharp
// Create a builder to organize dependencies and register data configurations.
var builder = new CrystalControl.Builder()
    .ConfigureCrystal(context =>
    {
        // Register FirstData configuration.
        context.AddCrystal<FirstData>(
            new CrystalConfiguration()
            {
                SavePolicy = SavePolicy.Manual, // Timing of saving data is controlled by the application.
                SaveFormat = SaveFormat.Utf8, // Format is utf8 text.
                NumberOfHistoryFiles = 0, // No history file.
                FileConfiguration = new LocalFileConfiguration("Local/SimpleExample/SimpleData.tinyhand"), // Specify the file name to save.
            });
    });

var unit = builder.Build(); // Build.
var crystalizer = unit.Context.ServiceProvider.GetRequiredService<Crystalizer>(); // Obtains a Crystalizer instance for data storage operations.
await crystalizer.PrepareAndLoadAll(false); // Prepare resources for storage operations and read data from files.

var data = unit.Context.ServiceProvider.GetRequiredService<FirstData>(); // Retrieve a data instance from the service provider.

Console.WriteLine($"Load {data.ToString()}"); // Id: 0 Name: Hoge
data.Id = 1;
data.Name = "Fuga";
Console.WriteLine($"Save {data.ToString()}"); // Id: 1 Name: Fuga

await crystalizer.SaveAll(); // Save all data.
```



## CrystalConfiguration




## Timing of data persistence
Data persistence is a core feature of CrystalData and its timing is critical. There are several options for when to save data.
The following code is for preparation.

```csharp
[TinyhandObject(Journaling = true)] // Journaling feature is necessary to allow the function to save data when properties are changed.
public partial class SaveTimingData
{
    [Key(0, AddProperty = "Id")] // Add a property to save data when the value is changed.
    internal int id;

    public override string ToString()
        => $"Id: {this.Id}";
}
```

```csharp
var crystal = unit.Context.ServiceProvider.GetRequiredService<ICrystal<SaveTimingData>>();
var data = crystal.Data;
```



### Instant save

Save the data after it has been changed, and wait until the process is complete.

```csharp
// Save instantly
data.id += 1;
await crystal.Save();
```



### On changed

When data is changed, it is registered in the save queue and will be saved in a second.

```csharp
context.AddCrystal<SaveTimingData>(
    new CrystalConfiguration()
    {
        SavePolicy = SavePolicy.OnChanged,
        FileConfiguration = new LocalFileConfiguration("Local/SaveTimingExample/SaveTimingData.tinyhand"), // Specify the file name to save.
    });
```

```csharp
// Add to the save queue when the value is changed
data.Id += 2;

// Alternative
data.id += 2;
crystal.TryAddToSaveQueue();
```




### Manual
Timing of saving data is controlled by the application.

```csharp
context.AddCrystal<SaveTimingData>(
    new CrystalConfiguration()
    {
        SavePolicy = SavePolicy.Manual, // Timing of saving data is controlled by the application.
        FileConfiguration = new LocalFileConfiguration("Local/SaveTimingExample/SaveTimingData.tinyhand"), // Specify the file name to save.
    });
```

```csharp
await crystal.Save();
```



### Periodic

By setting **SavePolicy** to **Periodic** in **CrystalConfiguration**, data can be saved at regular intervals.

```csharp
context.AddCrystal<SaveTimingData>(
    new CrystalConfiguration()
    {
        SavePolicy = SavePolicy.Periodic, // Data will be saved at regular intervals.
        SaveInterval = TimeSpan.FromMinutes(1), // The interval at which data is saved.
        SaveFormat = SaveFormat.Utf8, // Format is utf8 text.
        NumberOfHistoryFiles = 0, // No history file.
        FileConfiguration = new LocalFileConfiguration("Local/SaveTimingExample/SaveTimingData.tinyhand"), // Specify the file name to save.
    });
```




### When exiting the application
Add the following code to save all data and release resources when the application exits.

```csharp
await unit.Context.ServiceProvider.GetRequiredService<Crystalizer>().SaveAllAndTerminate();
```



### Volatile

Data is volatile and not saved.

```cahrp
context.AddCrystal<SaveTimingData>(
    new CrystalConfiguration()
    {
        SavePolicy = SavePolicy.Volatile,
        FileConfiguration = new LocalFileConfiguration("Local/SaveTimingExample/SaveTimingData.tinyhand"), // Specify the file name to save.
    });
```




## Timing of configuration and instantiation

### Builder pattern
Create a **CrystalControl.Builder** and register Data using the **ConfigureCrystal()** and **AddCrystal()** methods. As Data is registered in the DI container, it can be easily used.

```csharp
var builder = new CrystalControl.Builder()
    .Configure(context =>
    {
        context.AddSingleton<ConfigurationExampleClass>();
    })
    .ConfigureCrystal(context =>
    {
        // Register SimpleData configuration.
        context.AddCrystal<FirstData>(
            new CrystalConfiguration()
            {
                SavePolicy = SavePolicy.Manual, // Timing of saving data is controlled by the application.
                SaveFormat = SaveFormat.Utf8, // Format is utf8 text.
                NumberOfHistoryFiles = 0, // No history file.
                FileConfiguration = new LocalFileConfiguration("Local/FirstExample/FirstData.tinyhand"), // Specify the file name to save.
            });
    });

var unit = builder.Build(); // Build.
```

```csharp
public class ConfigurationExampleClass
{
    public ConfigurationExampleClass(Crystalizer crystalizer, FirstData firstData)
    {
        this.crystalizer = crystalizer;
        this.firstData = firstData;
    }
}
```



### Crystalizer
Create an **ICrystal** object using the **Crystalizer**.

If it's a new instance, make sure to register the configuration. If it has already been registered with the Builder, utilize the registered configuration.

```csharp
// Get or create an ICrystal interface of the data.
var crystal = this.crystalizer.GetOrCreateCrystal<SecondData>(
    new CrystalConfiguration(
        SavePolicy.Manual,
        new LocalFileConfiguration("Local/ConfigurationTimingExample/SecondData.tinyhand")));
var secondData = crystal.Data;

// You can create multiple crystals from single data class.
var crystal2 = this.crystalizer.CreateCrystal<SecondData>();
crystal2.Configure(new CrystalConfiguration(
        SavePolicy.Manual,
        new LocalFileConfiguration("Local/ConfigurationTimingExample/SecondData2.tinyhand")));
var secondData2 = crystal2.Data;
```



## Specifying a path

### Local path

### Relative path



## Data class
public class Data
{
}

Add lock object:
If you need exclusive access for multi-threading, please add Lock object



## Journaling



## AWS S3



## Template data class
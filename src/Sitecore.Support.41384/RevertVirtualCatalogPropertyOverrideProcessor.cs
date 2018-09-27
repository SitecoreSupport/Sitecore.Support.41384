// Sitecore.Commerce.Connect.CommerceServer.Catalog.Pipelines.RevertVirtualCatalogPropertyOverrideProcessor
using CommerceServer.Core.Catalog;
using Sitecore.Commerce.Connect.CommerceServer;
using Sitecore.Commerce.Connect.CommerceServer.Catalog;
using Sitecore.Commerce.Connect.CommerceServer.Catalog.Pipelines;
using Sitecore.Data;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sitecore.Support.Commerce.Connect.CommerceServer.Catalog.Pipelines
{
/// <summary>
/// Sitecore pipeline component that reverts the properties of a catalog item in a virtual catalog back to the original value defined in the base catalog.
/// </summary>
public class RevertVirtualCatalogPropertyOverrideProcessor
{
  private static readonly string[] CommonItemProperties = new string[2]
  {
        "DisplayName",
        "cy_list_price"
  };

  /// <summary>
  /// Reverts the properties of a catalog item in a virtual catalog back to the original value defined in the base catalog.
  /// </summary>
  /// <param name="args">The pipeline arguments.</param>
  public virtual void Process(RevertVirtualCatalogPropertyOverrideArgs args)
  {
    Assert.ArgumentNotNull(args, "args");
    Assert.ArgumentCondition(!ID.IsNullOrEmpty(args.InputParameters.ItemId), "args.InputParameters.ItemId", "A value must be provided for the args.InputParameters.ItemId argument.");
    args.OutputParameters.Success = false;
    ICatalogRepository catalogRepository = CommerceTypeLoader.CreateInstance<ICatalogRepository>();
    ExternalIdInformation externalIdInformation = catalogRepository.GetExternalIdInformation(args.InputParameters.ItemId.Guid);
    if (externalIdInformation == null || (externalIdInformation.CommerceItemType != CommerceItemType.Category && externalIdInformation.CommerceItemType != CommerceItemType.Product && externalIdInformation.CommerceItemType != CommerceItemType.ProductFamily && externalIdInformation.CommerceItemType != CommerceItemType.Variant))
    {
      string message = Translate.Text("The pipeline cannot be processed because the item with ID {0} does not represent a Commerce Server category, product, or variant", args.InputParameters.ItemId);
      CommerceLog.Current.Error(message, this);
    }
    else if (!catalogRepository.GetCatalog(externalIdInformation.CatalogName).IsVirtualCatalog)
    {
      string message2 = Translate.Text("The pipeline cannot be processed because the catalog item with ID {0} does not belong to a virtual catalog (CatalogName = \"{1}\").", args.InputParameters.ItemId, externalIdInformation.CatalogName);
      CommerceLog.Current.Error(message2, this);
    }
    else
    {
      Variant variant = null;
      CatalogItem catalogItem = null;
      ProductFamily productFamily = null;
      DefinitionPropertyType propertyType = DefinitionPropertyType.NormalProperty;
      switch (externalIdInformation.CommerceItemType)
      {
        case CommerceItemType.Category:
          catalogItem = catalogRepository.GetCategory(externalIdInformation.CatalogName, externalIdInformation.CategoryName, args.InputParameters.Language, true);
          break;
        case CommerceItemType.Product:
        case CommerceItemType.ProductFamily:
          catalogItem = catalogRepository.GetProduct(externalIdInformation.CatalogName, externalIdInformation.ProductId, args.InputParameters.Language, true);
          productFamily = (catalogItem as ProductFamily);
          break;
        case CommerceItemType.Variant:
          catalogItem = catalogRepository.GetProduct(externalIdInformation.CatalogName, externalIdInformation.ProductId, args.InputParameters.Language, true);
          variant = ((ProductFamily)catalogItem).GetVariant(externalIdInformation.VariantId);
          propertyType = DefinitionPropertyType.VariantProperty;
          break;
      }
      if (catalogItem == null)
      {
        string message3 = Translate.Text("The pipeline cannot be processed because the catalog item could not be found.  CommerceItemType = {0}, CatalogName = \"{1}\", CategoryName = \"{2}\", ProductId = \"{3}\", VariantId = \"{4}\", Language = \"{5}\"", externalIdInformation.CommerceItemType, externalIdInformation.CatalogName, externalIdInformation.CategoryName, externalIdInformation.ProductId, externalIdInformation.VariantId, args.InputParameters.Language);
        CommerceLog.Current.Error(message3, this);
      }
      else
      {
        IEnumerable<string> propertiesToRevert = GetPropertiesToRevert(args, catalogItem.DefinitionName, propertyType);
        if (args.InputParameters.RevertProductFamilyVariants && productFamily != null)
        {
          RevertProperties(propertiesToRevert, null, productFamily);
          foreach (Variant variant2 in productFamily.Variants)
          {
            RevertProperties(propertiesToRevert, variant2, null);
          }
        }
        else
        {
          RevertProperties(propertiesToRevert, variant, catalogItem);
        }
        //CatalogUtility.SaveCatalogItem(catalogItem, true);
        typeof(CatalogUtility).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(x => x.Name == "SaveCatalogItem").FirstOrDefault()
          .Invoke(null, new Object[] { catalogItem, true });
          if (variant != null)
        {
          CommerceUtility.RemoveItemFromSitecoreCaches(new ID(variant.ExternalId), null);
        }
        args.OutputParameters.Success = true;
      }
    }
  }

  /// <summary>
  /// Gets the properties that should be reverted on the specified item.
  /// </summary>
  /// <param name="args">The pipeline arguments.</param>
  /// <param name="definitionName">The name of the catalog item definition.</param>
  /// <param name="propertyType">The type of of properties that should be returned (variant or normal).</param>
  /// <returns>The properties that should be reverted on the specified item.</returns>
  protected virtual IEnumerable<string> GetPropertiesToRevert(RevertVirtualCatalogPropertyOverrideArgs args, string definitionName, DefinitionPropertyType propertyType)
  {
    if (args.InputParameters.PropertiesToRevert != null && args.InputParameters.PropertiesToRevert.Length != 0)
    {
      return args.InputParameters.PropertiesToRevert;
    }
    return GetPropertiesFromDefinition(definitionName, propertyType);
  }

  /// <summary>
  /// Gets the properties that are assigned to a catalog item definition.
  /// </summary>
  /// <param name="definitionName">The name of the catalog item definition.</param>
  /// <param name="propertyType">The type of property definitions to return (normal or variant).</param>
  /// <returns>The properties that are assigned to the catalog item definition.</returns>
  protected virtual IEnumerable<string> GetPropertiesFromDefinition(string definitionName, DefinitionPropertyType propertyType)
  {
    CatalogDefinition catalogDefinition = CommerceTypeLoader.CreateInstance<ICatalogRepository>().GetCatalogDefinition(definitionName);
    foreach (CatalogDefinitionPropertiesDataSet.CatalogDefinitionProperty catalogDefinitionProperty in catalogDefinition.DefinitionProperties.CatalogDefinitionProperties)
    {
      if (catalogDefinitionProperty.PropertyType == (int)propertyType)
      {
        yield return catalogDefinitionProperty.PropertyName;
      }
    }
    string[] commonItemProperties = CommonItemProperties;
    foreach (string text in commonItemProperties)
    {
      yield return text;
    }
  }

  private void RevertProperties(IEnumerable<string> propertiesToRevert, Variant variant, CatalogItem catalogItem)
  {
    foreach (string item in propertiesToRevert)
    {
      if (variant != null)
      {
        if (!variant.DataRow.Table.Columns[item].ReadOnly)
        {
          variant[item] = DBNull.Value;
        }
      }
      else if (!catalogItem.Information.CatalogItems.Columns[item].ReadOnly)
      {
        ((CatalogObject)catalogItem)[item] = DBNull.Value;
      }
    }
  }
}
}

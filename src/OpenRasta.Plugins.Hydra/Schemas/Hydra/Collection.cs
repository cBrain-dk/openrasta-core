﻿using System;
using System.Collections;
using Newtonsoft.Json;
using OpenRasta.Configuration.MetaModel;
using OpenRasta.Plugins.Hydra.Configuration;

namespace OpenRasta.Plugins.Hydra.Schemas.Hydra
{
  
  public class Collection : JsonLd.INode
  {
    public static CollectionWithIdentifier FromModel(Uri appBase, ResourceModel itemModel, HydraUriModel uriModel)
    {
      var collection = new CollectionWithIdentifier
      {
        Identifier = new Uri(appBase, new Uri(uriModel.EntryPointUri, UriKind.RelativeOrAbsolute)),
        Search = uriModel.SearchTemplate,
      };
      
      if (itemModel.Hydra().ManagesBlockType != null)
        collection.Manages.Object = itemModel.Hydra().ManagesBlockType;
      return collection;
    }

    protected Collection()
    {
    }
    
    public IriTemplate Search { get; set; }

    public CollectionManages Manages { get; } = new CollectionManages();
    public virtual int? TotalItems => null;

  }

  public class CollectionWithIdentifier : Collection
  {
    [JsonProperty("@id")]
    public Uri Identifier { get; set; }
  }
}
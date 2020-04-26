﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using OpenRasta.Codecs;
using OpenRasta.DI;
using OpenRasta.Diagnostics;
using OpenRasta.OperationModel.Hydrators.Diagnostics;
using OpenRasta.TypeSystem.ReflectionBased;
using OpenRasta.Web;
using OpenRasta.TypeSystem;

namespace OpenRasta.OperationModel.Hydrators
{
  // TODO: Rewrite, this is unreadable.
  public class RequestEntityReaderHydrator : IRequestEntityReader
  {
    readonly IRequest _request;
    readonly IDependencyResolver _resolver;

    public RequestEntityReaderHydrator(IDependencyResolver resolver, IRequest request)
    {
      Log = NullLogger<CodecLogSource>.Instance;
      ErrorCollector = NullErrorCollector.Instance;
      _resolver = resolver;
      _request = request;
    }

    public IErrorCollector ErrorCollector { get; set; }
    public ILogger<CodecLogSource> Log { get; set; }

    static IOperationAsync TryGetUnreadyWithCodec(IEnumerable<IOperationAsync> operations)
    {
      return VerifySingleMatch((
        from o in operations
        let codecMatch = o.GetRequestCodec()
        where codecMatch != null
        orderby codecMatch descending
        group o by new
        {
          codecMatch.WeightedScore,
          codecMatch.MatchingParameterCount
        }
      ).FirstOrDefault());
    }

    static IOperationAsync SelectReady(IEnumerable<IOperationAsync> operations)
    {
      return VerifySingleMatch((
        from o in operations
        where o.Inputs.AllReady()
        orderby o.Inputs.CountReady() descending
        group o by o.Inputs.CountReady()
      ).FirstOrDefault());
    }

    static IOperationAsync VerifySingleMatch(IEnumerable<IOperationAsync> operations)
    {
      if (operations == null) return null;

      if (operations.Count() > 1)
        throw new AmbiguousRequestException(operations);
      return operations.Single();
    }

    async Task<Tuple<RequestReadResult, IOperationAsync>> ReadWithCodec(IOperationAsync operation)
    {
      var codecInstance = CreateMediaTypeReader(operation);

      var codecType = codecInstance.GetType();
      Log.CodecLoaded(codecType);

      if (codecType.Implements(typeof(IKeyedValuesMediaTypeReader<>)))
        return Tuple.Create(
          TryAssignKeyedValues(_request.Entity, codecInstance, codecType, operation),
          operation);

      return Tuple.Create(await TryReadPayloadAsObject(
          _request.Entity,
          GetReader(codecInstance),
          operation),
        operation);
    }

    static Func<IHttpEntity, IType, string, Task<object>> GetReader(ICodec instance)
    {
      if (instance is IMediaTypeReaderAsync readerAsync)
        return readerAsync.ReadFrom;

      return (obj, type, name) => Task.FromResult(
        ((IMediaTypeReader) instance).ReadFrom(obj, type, name));
    }

    public Task<Tuple<RequestReadResult, IOperationAsync>> Read(IEnumerable<IOperationAsync> operations)
    {
      var operationAsyncs = operations as IOperationAsync[] ?? operations.ToArray();

      var opsAlreadyReady = operationAsyncs.Where(op => op.Inputs.AllReady()).ToArray();
      var opWithCodec = TryGetUnreadyWithCodec(operationAsyncs);

      return opWithCodec == null ?
        Task.FromResult(ReturnTrySelectReady(operationAsyncs)) : 
        TryWithCodec(opsAlreadyReady, opWithCodec);
    }

    static Tuple<RequestReadResult, IOperationAsync> ReturnTrySelectReady(IOperationAsync[] operationAsyncs)
    {
      var ready = SelectReady(operationAsyncs);
      return
        ready == null
          ? Tuple.Create<RequestReadResult, IOperationAsync>(RequestReadResult.NoneFound, null)
          : Tuple.Create(RequestReadResult.Success, ready);
    }

    async Task<Tuple<RequestReadResult, IOperationAsync>> TryWithCodec(IOperationAsync[] opsAlreadyReady,
      IOperationAsync opWithCodec)
    {
      var tryRead = await ReadWithCodec(opWithCodec);
      
      if (tryRead.Item1 == RequestReadResult.Success && tryRead.Item2.Inputs.AllReady())
        return tryRead;

      return ReturnTrySelectReady(opsAlreadyReady);
    }

    static ErrorFrom<RequestEntityReaderHydrator> CreateErrorForException(Exception e)
    {
      return new ErrorFrom<RequestEntityReaderHydrator>
      {
        Message = "The codec failed to process the request entity. See the exception below.\r\n" + e,
        Exception = e
      };
    }


    ICodec CreateMediaTypeReader(IOperationAsync operation)
    {
      // TODO: Evil crap, should auto-register and allow container to deal with insta

      return
        _resolver.Resolve(operation.GetRequestCodec().CodecRegistration.CodecType, UnregisteredAction.AddAsTransient) as
          ICodec;
    }

    RequestReadResult TryAssignKeyedValues(IHttpEntity requestEntity, ICodec codec, Type codecType,
      IOperationAsync operation)
    {
      Log.CodecSupportsKeyedValues();

      return codec.TryAssignKeyValues(requestEntity, operation.Inputs.Select(x => x.Binder), Log.KeyAssigned,
        Log.KeyFailed)
        ? RequestReadResult.Success
        : RequestReadResult.CodecFailure;
    }

    async Task<RequestReadResult> TryReadPayloadAsObject(IHttpEntity requestEntity,
      Func<IHttpEntity, IType, string, Task<object>> reader, IOperationAsync operation)
    {
      Log.CodecSupportsFullObjectResolution();
      foreach (var member in operation.Inputs.Where(m => m.Binder.IsEmpty))
      {
        Log.ProcessingMember(member);
        try
        {
          var entityInstance = await reader(requestEntity,
            member.Member.Type,
            member.Member.Name);
          Log.Result(entityInstance);

          if (entityInstance == Missing.Value)
            continue;

          if (!member.Binder.SetInstance(entityInstance))
          {
            Log.BinderInstanceAssignmentFailed();
            return RequestReadResult.BinderFailure;
          }

          Log.BinderInstanceAssignmentSucceeded();
          return RequestReadResult.Success;
        }
        catch (Exception e)
        {
          ErrorCollector.AddServerError(CreateErrorForException(e));
          return RequestReadResult.CodecFailure;
        }
      }

      return RequestReadResult.CodecMissingValue;
    }
  }
}
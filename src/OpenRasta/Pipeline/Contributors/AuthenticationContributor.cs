﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using OpenRasta.Authentication;
using OpenRasta.Diagnostics;
using OpenRasta.Web;

namespace OpenRasta.Pipeline.Contributors
{
  [Obsolete(
    "Authentication features are moving to a new package, see more information at http://https://github.com/openrasta/openrasta/wiki/Authentication")]
  public class AuthenticationContributor : KnownStages.IAuthentication
  {
    readonly Func<IEnumerable<IAuthenticationScheme>> _authSchemes;
    public ILogger Log { get; set; }

    public AuthenticationContributor(Func<IEnumerable<IAuthenticationScheme>> authSchemes)
    {
      _authSchemes = authSchemes;
    }

    public void Initialize(IPipeline pipelineRunner)
    {
      pipelineRunner.Notify(AuthoriseRequest)
        .After<KnownStages.IBegin>()
        .And
        .Before<KnownStages.IHandlerSelection>();
    }

    private PipelineContinuation AuthoriseRequest(ICommunicationContext context)
    {
      var requestedAuthSchemeName = ExtractRequestedAuthScheme(context.Request);

      if (requestedAuthSchemeName == null)
        return PipelineContinuation.Continue;

      var schemeToUse = _authSchemes().SingleOrDefault(
        scheme => string.Equals(scheme.Name, requestedAuthSchemeName,
        StringComparison.OrdinalIgnoreCase));

      if (schemeToUse == null)
      {
        context.Response.Headers["Warning"] = "199 Unsupported Authentication Scheme";
        return PipelineContinuation.Continue;
      }

      var authResult = schemeToUse.Authenticate(context.Request);

      if (authResult is AuthenticationResult.Success)
      {
        var success = (authResult as AuthenticationResult.Success);
        context.User = CreatePrincipal(success, schemeToUse);
      }

      if (authResult is AuthenticationResult.MalformedCredentials)
      {
        context.OperationResult = new OperationResult.BadRequest();
        context.Response.Headers["Warning"] = "199 Malformed credentials";
        return PipelineContinuation.RenderNow;
      }

      if (authResult is AuthenticationResult.Failed)
      {
        context.OperationResult = new OperationResult.Unauthorized();
      }

      return PipelineContinuation.Continue;
    }

    static string ExtractRequestedAuthScheme(IRequest request)
    {
      var authRequestHeader = request.Headers["Authorization"];

      if (string.IsNullOrEmpty(authRequestHeader))
        return null;

      var requestedAuthSchemeName = authRequestHeader.Split(' ')[0];

      if (string.IsNullOrEmpty(requestedAuthSchemeName))
        return null;

      return requestedAuthSchemeName;
    }

    static IPrincipal CreatePrincipal(AuthenticationResult.Success success, IAuthenticationScheme scheme)
    {
      var identity = new GenericIdentity(success.Username, scheme.Name);
      return new GenericPrincipal(identity, success.Roles);
    }
  }
}
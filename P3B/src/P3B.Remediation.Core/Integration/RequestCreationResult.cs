using System.Diagnostics.CodeAnalysis;
using P3B.Remediation.Core.Actions;

namespace P3B.Remediation.Core.Integration;

/// <summary>Either a valid <see cref="RemediationRequest"/> or the problems that prevented one.</summary>
public sealed record RequestCreationResult
{
    private RequestCreationResult(RemediationRequest? request, IEnumerable<string> problems)
    {
        Request = request;
        Problems = Array.AsReadOnly(problems.ToArray());
    }

    public RemediationRequest? Request { get; }

    public IReadOnlyList<string> Problems { get; }

    [MemberNotNullWhen(true, nameof(Request))]
    public bool IsValid => Request is not null;

    public static RequestCreationResult Valid(RemediationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new RequestCreationResult(request, Array.Empty<string>());
    }

    public static RequestCreationResult Invalid(IEnumerable<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        var list = problems.ToArray();
        if (list.Length == 0)
        {
            throw new ArgumentException("An invalid result must state at least one problem.", nameof(problems));
        }

        return new RequestCreationResult(request: null, list);
    }
}

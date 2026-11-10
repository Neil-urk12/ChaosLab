namespace Backend.Features.Members;

public static class MemberEndpoints
{
    public static void MapMemberEndpoints(this IEndpointRouteBuilder api)
    {
        var members = api.MapGroup("/projects/{id:guid}/members").WithTags("Members");
        members.MapGet("/", ListMembers.Handle);
        members.MapGet("/candidates", SearchCandidates.Handle);
        members.MapPost("/", AddMember.Handle);
        members.MapPatch("/{userId:guid}", ChangeMemberRole.Handle);
        members.MapDelete("/{userId:guid}", RemoveMember.Handle);
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bit.Core.Repositories;
using Bit.Core.Services;
using Bit.Scim.Context;
using Bit.Scim.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bit.Scim.Controllers.v2
{
    [Authorize("Scim")]
    [Route("v2/{organizationId}/groups")]
    public class GroupsController : Controller
    {
        private readonly ScimSettings _scimSettings;
        private readonly IGroupRepository _groupRepository;
        private readonly IGroupService _groupService;
        private readonly IScimContext _scimContext;
        private readonly ILogger<GroupsController> _logger;

        public GroupsController(
            IGroupRepository groupRepository,
            IGroupService groupService,
            IOptions<ScimSettings> scimSettings,
            IScimContext scimContext,
            ILogger<GroupsController> logger)
        {
            _scimSettings = scimSettings?.Value;
            _groupRepository = groupRepository;
            _groupService = groupService;
            _scimContext = scimContext;
            _logger = logger;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid organizationId, Guid id)
        {
            var group = await _groupRepository.GetByIdAsync(id);
            if (group == null || group.OrganizationId != organizationId)
            {
                return new NotFoundObjectResult(new ScimErrorResponseModel
                {
                    Status = 404,
                    Detail = "Group not found."
                });
            }
            return new ObjectResult(new ScimGroupResponseModel(group));
        }

        [HttpGet("")]
        public async Task<IActionResult> Get(
            Guid organizationId,
            [FromQuery] int? count,
            [FromQuery] int? startIndex)
        {
            var groups = await _groupRepository.GetManyByOrganizationIdAsync(organizationId);
            var groupList = groups.OrderBy(g => g.Name)
                .Skip(startIndex.Value - 1)
                .Take(count.Value)
                .Select(g => new ScimGroupResponseModel(g))
                .ToList();

            var result = new ScimListResponseModel<ScimGroupResponseModel>
            {
                Resources = groupList,
                ItemsPerPage = count.GetValueOrDefault(groupList.Count),
                TotalResults = groups.Count,
                StartIndex = startIndex.GetValueOrDefault(1),
            };
            return new ObjectResult(result);
        }

        [HttpPost("")]
        public async Task<IActionResult> Post(Guid organizationId, [FromBody] ScimGroupRequestModel model)
        {
            if (string.IsNullOrWhiteSpace(model.DisplayName))
            {
                return new BadRequestResult();
            }

            var groups = await _groupRepository.GetManyByOrganizationIdAsync(organizationId);
            if (!string.IsNullOrWhiteSpace(model.ExternalId) && groups.Any(g => g.ExternalId == model.ExternalId))
            {
                return new ConflictResult();
            }

            var group = model.ToGroup(organizationId);
            await _groupService.SaveAsync(group, null);
            var response = new ScimGroupResponseModel(group);
            return new CreatedResult(Url.Action(nameof(Get), new { group.OrganizationId, group.Id }), response);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(Guid organizationId, Guid id, [FromBody] ScimGroupRequestModel model)
        {
            var group = await _groupRepository.GetByIdAsync(id);
            if (group == null || group.OrganizationId != organizationId)
            {
                return new NotFoundObjectResult(new ScimErrorResponseModel
                {
                    Status = 404,
                    Detail = "Group not found."
                });
            }

            group.Name = model.DisplayName;
            await _groupService.SaveAsync(group);
            return new ObjectResult(new ScimGroupResponseModel(group));
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> Patch(Guid organizationId, Guid id, [FromBody] ScimPatchModel model)
        {
            var group = await _groupRepository.GetByIdAsync(id);
            if (group == null || group.OrganizationId != organizationId)
            {
                return new NotFoundObjectResult(new ScimErrorResponseModel
                {
                    Status = 404,
                    Detail = "Group not found."
                });
            }

            var replaceOp = model.Operations?.FirstOrDefault(o => o.Op == "replace");
            if (replaceOp != null)
            {
                // Replace a list of members
                if (replaceOp.Path == "members")
                {
                    var ids = GetOperationValueIds(replaceOp.Value);
                    await _groupRepository.UpdateUsersAsync(group.Id, ids);
                }
                // Replace group name
                else if (replaceOp.Value.TryGetProperty("displayName", out var displayNameProperty))
                {
                    group.Name = displayNameProperty.GetString();
                }
            }

            // Add a single member
            var addMemberOp = model.Operations?.FirstOrDefault(
                o => o.Op == "add" && !string.IsNullOrWhiteSpace(o.Path) && o.Path.StartsWith("members[value eq "));
            if (addMemberOp != null)
            {
                var addId = GetOperationPathId(addMemberOp.Path);
                if (addId.HasValue)
                {
                    var orgUserIds = (await _groupRepository.GetManyUserIdsByIdAsync(group.Id)).ToHashSet();
                    orgUserIds.Add(addId.Value);
                    await _groupRepository.UpdateUsersAsync(group.Id, orgUserIds);
                }
            }

            // Add a list of members
            var addMembersOp = model.Operations?.FirstOrDefault(o => o.Op == "add" && o.Path == "members");
            if (addMembersOp != null)
            {
                var orgUserIds = (await _groupRepository.GetManyUserIdsByIdAsync(group.Id)).ToHashSet();
                foreach (var v in GetOperationValueIds(addMembersOp.Value))
                {
                    orgUserIds.Add(v);
                }
                await _groupRepository.UpdateUsersAsync(group.Id, orgUserIds);
            }

            // Remove a single member
            var removeMemberOp = model.Operations?.FirstOrDefault(
                o => o.Op == "remove" && !string.IsNullOrWhiteSpace(o.Path) && o.Path.StartsWith("members[value eq "));
            if (removeMemberOp != null)
            {
                var removeId = GetOperationPathId(removeMemberOp.Path);
                if (removeId.HasValue)
                {
                    await _groupService.DeleteUserAsync(group, removeId.Value);
                }
            }

            // Remove a list of members
            var removeMembersOp = model.Operations?.FirstOrDefault(o => o.Op == "remove" && o.Path == "members");
            if (removeMembersOp != null)
            {
                var orgUserIds = (await _groupRepository.GetManyUserIdsByIdAsync(group.Id)).ToHashSet();
                foreach (var v in GetOperationValueIds(removeMembersOp.Value))
                {
                    orgUserIds.Remove(v);
                }
                await _groupRepository.UpdateUsersAsync(group.Id, orgUserIds);
            }

            await _groupService.SaveAsync(group);
            return new NoContentResult();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid organizationId, Guid id)
        {
            var group = await _groupRepository.GetByIdAsync(id);
            if (group == null || group.OrganizationId != organizationId)
            {
                return new NotFoundObjectResult(new ScimErrorResponseModel
                {
                    Status = 404,
                    Detail = "Group not found."
                });
            }
            await _groupService.DeleteAsync(group);
            return new NoContentResult();
        }

        private List<Guid> GetOperationValueIds(JsonElement objArray)
        {
            var ids = new List<Guid>();
            foreach (var obj in objArray.EnumerateArray())
            {
                if (obj.TryGetProperty("value", out var valueProperty))
                {
                    if (valueProperty.TryGetGuid(out var guid))
                    {
                        ids.Add(guid);
                    }
                }
            }
            return ids;
        }

        private Guid? GetOperationPathId(string path)
        {
            // Parse Guid from string like: members[value eq "{GUID}"}]
            if (Guid.TryParse(path.Substring(18).Replace("\"]", string.Empty), out var id))
            {
                return id;
            }
            return null;
        }
    }
}

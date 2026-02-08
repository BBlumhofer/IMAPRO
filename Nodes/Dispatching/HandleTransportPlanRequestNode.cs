using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AasSharpClient.Models;
using AasSharpClient.Models.Helpers;
using AasSharpClient.Models.Messages;
using AasSharpClient.Models.ManufacturingSequence;
using AasSharpClient.Models.ProcessChain;
using MAS_BT.Core;
using Microsoft.Extensions.Logging;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Core;
using I40Sharp.Messaging.Models;
using BaSyx.Models.AdminShell;
using AasSharpClient.Tools;
using MAS_BT.Services.Transport;
using MAS_BT.Tools;
using Neo4j.Driver;
using ActionModel = AasSharpClient.Models.Action;

namespace MAS_BT.Nodes.Dispatching
{
    public class HandleTransportPlanRequestNode : BTNode
    {
        private readonly SemaphoreSlim _plannerInitLock = new(1, 1);
        private ITransportManagerPlanner? _planner;
        private IDriver? _neo4jDriver;

        public HandleTransportPlanRequestNode() : base("HandleTransportPlanRequest") { }

        public override async Task<NodeStatus> Execute()
        {
            var client = Context.Get<MessagingClient>("MessagingClient");
            var incoming = Context.Get<I40Message>("LastReceivedMessage");
            if (client == null || incoming == null)
            {
                Logger.LogWarning("HandleTransportPlanRequest: missing client or message");
                return NodeStatus.Failure;
            }

            var ns = Context.Get<string>("config.Namespace") ?? Context.Get<string>("Namespace") ?? "phuket";
            var responseTopic = $"/{ns}/TransportPlan/Response";
            var conversationId = incoming.Frame?.ConversationId ?? Guid.NewGuid().ToString();
            var requesterId = incoming.Frame?.Sender?.Identification?.Id ?? "Unknown";
            var requesterRole = incoming.Frame?.Sender?.Role?.Name ?? "Unknown";

            try
            {
                // Ensure a minimal, always-visible trace for test logs and CI: write to Console.
                try { Console.WriteLine($"[DEBUG] HandleTransportPlanRequest: start conversationId={conversationId} requester={requesterId} role={requesterRole} elements={incoming?.InteractionElements?.Count ?? 0}"); } catch {}
                LogIncomingMessage(incoming, conversationId);

                var request = ParseTransportRequest(incoming);
                try {
                    Console.WriteLine($"[DEBUG] HandleTransportPlanRequest: parsed request present={(request!=null)} start={request?.TransportStartStationText} goal={request?.TransportGoalStationText}");
                    Console.WriteLine($"[DEBUG] HandleTransportPlanRequest: IdentifierType={request?.IdentifierTypeText} IdentifierValue={request?.IdentifierValueText}");
                    try
                    {
                        var idx = Context.Get<Dictionary<string, AasSharpClient.Models.ManufacturingSequence.ManufacturingSequence>>("ManufacturingSequence.ByProduct");
                        Console.WriteLine($"[DEBUG] HandleTransportPlanRequest: ManufacturingSequence.ByProduct present={(idx!=null)}");
                    }
                    catch
                    {
                        Console.WriteLine($"[DEBUG] HandleTransportPlanRequest: ManufacturingSequence.ByProduct present=False");
                    }
                } catch {}

                if (request == null)
                {
                    await PublishRefusalAsync(client, responseTopic, requesterId, requesterRole, conversationId,
                        "Invalid payload: expected SubmodelElementCollection in interaction elements",
                        "Dispatching agent requires a SubmodelElementCollection as the first interaction element.")
                        .ConfigureAwait(false);
                    return NodeStatus.Failure;
                }

                EnsureTransportStartStation(request, incoming);
                var planner = await GetPlannerAsync().ConfigureAwait(false);
                var planResult = await planner.BuildPlanAsync(request).ConfigureAwait(false);

                var capabilitySequence = BuildCapabilitySequence(request, planResult, requesterId);
                request.CapabilitiesSequence.Clear();
                request.CapabilitiesSequence.InsertRangeAfter(capabilitySequence.Capabilities);

                var senderId = string.IsNullOrWhiteSpace(Context.AgentId) ? "DispatchingAgent" : Context.AgentId;
                var senderRole = string.IsNullOrWhiteSpace(Context.AgentRole) ? "DispatchingAgent" : Context.AgentRole;

                var responseMsg = new TransportPlanResponseMessage(
                    senderId,
                    senderRole,
                    requesterId,
                    requesterRole,
                    conversationId,
                    request);

                await responseMsg.PublishAsync(client, responseTopic, senderId, senderRole, requesterId, requesterRole, conversationId)
                    .ConfigureAwait(false);

                Logger.LogInformation(
                    "HandleTransportPlanRequest: sent transport plan with {Legs} capability leg(s) (conversationId={ConversationId})",
                    capabilitySequence.Count,
                    conversationId);

                return NodeStatus.Success;
            }
            catch (Neo4j.Driver.AuthenticationException neoAuthEx)
            {
                try { Console.WriteLine($"[ERROR] HandleTransportPlanRequest: Neo4j authentication failed: {neoAuthEx}"); } catch {}
                Logger.LogWarning(neoAuthEx, "HandleTransportPlanRequest: Neo4j authentication failed");
                await PublishRefusalAsync(client, responseTopic, requesterId, requesterRole, conversationId,
                    "Neo4j authentication failed",
                    neoAuthEx.Message).ConfigureAwait(false);
                return NodeStatus.Failure;
            }
            catch (System.Security.Authentication.AuthenticationException authEx)
            {
                Logger.LogWarning(authEx, "HandleTransportPlanRequest: authentication failed");
                await PublishRefusalAsync(client, responseTopic, requesterId, requesterRole, conversationId,
                    "Authentication failed",
                    authEx.Message).ConfigureAwait(false);
                return NodeStatus.Failure;
            }
            catch (ServiceUnavailableException svcEx)
            {
                Logger.LogWarning(svcEx, "HandleTransportPlanRequest: Neo4j unavailable");
                await PublishRefusalAsync(client, responseTopic, requesterId, requesterRole, conversationId,
                    "Neo4j unavailable",
                    svcEx.Message).ConfigureAwait(false);
                return NodeStatus.Failure;
            }
            catch (InvalidOperationException planEx)
            {
                    try { Console.WriteLine($"[ERROR] HandleTransportPlanRequest: planning failed: {planEx.Message}"); } catch {}
                    Logger.LogWarning(planEx, "HandleTransportPlanRequest: planning failed");
                await PublishRefusalAsync(client, responseTopic, requesterId, requesterRole, conversationId,
                    "Transport planning failed",
                    planEx.Message).ConfigureAwait(false);
                return NodeStatus.Failure;
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[ERROR] HandleTransportPlanRequest: unexpected failure: {ex}"); } catch {}
                Logger.LogError(ex, "HandleTransportPlanRequest: unexpected failure");
                await PublishRefusalAsync(client, responseTopic, requesterId, requesterRole, conversationId,
                    "Transport planning error",
                    ex.Message).ConfigureAwait(false);
                return NodeStatus.Failure;
            }
        }

        private void LogIncomingMessage(I40Message incomingMessage, string conversationId)
        {
            if (incomingMessage == null)
            {
                Logger.LogWarning("HandleTransportPlanRequest: missing incoming message for conversationId={ConversationId}", conversationId);
                return;
            }

            var elementCount = incomingMessage.InteractionElements?.Count ?? 0;
            Logger.LogInformation(
                "HandleTransportPlanRequest: received request conversationId={ConversationId} elements={Count}",
                conversationId,
                elementCount);

            if (incomingMessage.InteractionElements == null)
            {
                return;
            }

            foreach (var element in incomingMessage.InteractionElements)
            {
                if (element is SubmodelElementCollection collection)
                {
                    Logger.LogDebug(
                        "HandleTransportPlanRequest: element {IdShort} children={Count}",
                        collection.IdShort,
                        collection.Count);

                    foreach (var child in collection)
                    {
                        var value = UnwrapValueFromElement(child);
                        Logger.LogTrace(
                            "HandleTransportPlanRequest: child {IdShort} value={Value}",
                            child?.IdShort,
                            value);
                    }
                }
                else
                {
                    Logger.LogDebug("HandleTransportPlanRequest: element type={Type}", element?.GetType().Name);
                }
            }
        }

        private TransportRequestMessage? ParseTransportRequest(I40Message incomingMessage)
        {
            if (incomingMessage == null)
            {
                return null;
            }

            var collection = incomingMessage.InteractionElements?
                .OfType<SubmodelElementCollection>()
                .FirstOrDefault();

            if (collection != null)
            {
                return new TransportRequestMessage(collection);
            }

            return BuildTransportRequestFromElements(incomingMessage);
        }

        private static TransportRequestMessage? BuildTransportRequestFromElements(I40Message incomingMessage)
        {
            if (incomingMessage?.InteractionElements == null || incomingMessage.InteractionElements.Count == 0)
            {
                return null;
            }

            var goal = ExtractValue(incomingMessage, "TransportGoalStation") ?? "Unknown";
            var identifierValue = ExtractValue(incomingMessage, "IdentifierValue") ?? "Unknown";
            var identifierType = ExtractValue(incomingMessage, "IdentifierType") ?? "Unknown";
            var amount = ExtractValue(incomingMessage, "Amount") ?? "1";

            var request = new TransportRequestMessage("TransportRequest");
            request.InstanceIdentifier.Value = new PropertyValue<string>($"transportreq_{Guid.NewGuid():N}");
            request.TransportStartStation.Value = new PropertyValue<string>("Storage");
            request.TransportGoalStation.Value = new PropertyValue<string>(goal);
            request.IdentifierType.Value = new PropertyValue<string>(identifierType);
            request.IdentifierValue.Value = new PropertyValue<string>(identifierValue);
            if (int.TryParse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAmount))
            {
                request.SetAmount(parsedAmount);
            }

            return request;
        }

        private async Task PublishRefusalAsync(
            MessagingClient client,
            string topic,
            string receiverId,
            string receiverRole,
            string conversationId,
            string reason,
            string detail)
        {
            if (client == null)
            {
                return;
            }

            var senderId = string.IsNullOrWhiteSpace(Context.AgentId) ? "TransportManager" : Context.AgentId;
            var senderRole = string.IsNullOrWhiteSpace(Context.AgentRole) ? "TransportManager" : Context.AgentRole;

            var refusal = new PlanningRefusalMessage(
                senderId,
                senderRole,
                string.IsNullOrWhiteSpace(receiverId) ? "Unknown" : receiverId,
                string.IsNullOrWhiteSpace(receiverRole) ? "Unknown" : receiverRole,
                conversationId,
                reason ?? "Transport plan request refused",
                detail ?? string.Empty);

            await refusal.PublishAsync(client, topic ?? string.Empty).ConfigureAwait(false);
            Logger.LogInformation("HandleTransportPlanRequest: published refusal for conversationId={ConversationId}", conversationId);
        }

        private async Task<ITransportManagerPlanner> GetPlannerAsync()
        {
            if (_planner != null)
            {
                return _planner;
            }

            await _plannerInitLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_planner != null)
                {
                    return _planner;
                }

                var uri = GetConfigString("Neo4j", "Uri");
                var user = GetConfigString("Neo4j", "Username") ?? GetConfigString("Neo4j", "User");
                var password = GetConfigString("Neo4j", "Password");
                var database = GetConfigString("Neo4j", "Database");

                // Fallback to environment variables or a common local test Neo4j instance if config missing
                if (string.IsNullOrWhiteSpace(uri))
                {
                    var envUri = Environment.GetEnvironmentVariable("NEO4J_URI");
                    var envUser = Environment.GetEnvironmentVariable("NEO4J_USER");
                    var envPass = Environment.GetEnvironmentVariable("NEO4J_PASSWORD");
                    if (!string.IsNullOrWhiteSpace(envUri)) uri = envUri;
                    if (!string.IsNullOrWhiteSpace(envUser)) user = envUser;
                    if (!string.IsNullOrWhiteSpace(envPass)) password = envPass;
                }

                if (string.IsNullOrWhiteSpace(uri))
                {
                    // best-effort default for local debug runs; override with env vars in CI
                    uri = "bolt://192.168.178.30:7687";
                    user = user ?? "neo4j";
                    password = password ?? "testtest";
                    Console.WriteLine($"[DEBUG] HandleTransportPlanRequest: using fallback Neo4j uri={uri} user={user}");
                }
                var costPerMeter = GetConfigDouble(1.0, "TransportPlanning", "CostPerMeter");

                if (string.IsNullOrWhiteSpace(uri))
                {
                    throw new InvalidOperationException("TransportManager configuration missing Neo4j.Uri");
                }

                if (_neo4jDriver == null)
                {
                    _neo4jDriver = GraphDatabase.Driver(uri, AuthTokens.Basic(user ?? string.Empty, password ?? string.Empty));
                }

                var graphQuery = new Neo4jTransportGraphQuery(_neo4jDriver, database);
                var catalogProvider = new Neo4jTransportCapabilityCatalogProvider(_neo4jDriver, database);
                _planner = new TransportManagerPlanner(graphQuery, catalogProvider, costPerMeter);

                Logger.LogInformation("HandleTransportPlanRequest: transport planner initialized (costPerMeter={Cost})", costPerMeter);
                return _planner;
            }
            finally
            {
                _plannerInitLock.Release();
            }
        }

        private CapabilitySequence BuildCapabilitySequence(
            TransportRequestMessage request,
            TransportManagerPlanResult planResult,
            string requesterId)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (planResult == null) throw new ArgumentNullException(nameof(planResult));

            var capabilityLegs = planResult.CapabilitySequence?.Legs;
            if (capabilityLegs == null || capabilityLegs.Count == 0)
            {
                throw new InvalidOperationException("Planner did not return any capability legs.");
            }

            var offers = new List<OfferedCapability>(capabilityLegs.Count);
            var scheduleAnchor = DateTime.UtcNow;
            var transportLegs = new Queue<TransportLeg>(planResult.Plan?.Legs ?? Array.Empty<TransportLeg>());
            var placementIndex = 1;

            foreach (var leg in capabilityLegs)
            {
                if (!planResult.Catalog.TryGetCapability(leg.ModuleId, leg.CapabilityIdShort, out var spec))
                {
                    throw new InvalidOperationException(
                        $"Capability '{leg.CapabilityIdShort}' for module '{leg.ModuleId}' missing in catalog.");
                }

                var (distance, cost) = DetermineCapabilityMetrics(leg, transportLegs);
                var offer = CreateCapabilityOffer(spec, request, requesterId, scheduleAnchor, placementIndex, distance, cost);
                offers.Add(offer);
                placementIndex++;
            }

            if (transportLegs.Count > 0)
            {
                Logger.LogDebug("HandleTransportPlanRequest: unused transport leg metrics count={Count}", transportLegs.Count);
            }

            var sequence = new CapabilitySequence();
            sequence.InsertRangeAfter(offers);
            return sequence;
        }

        private OfferedCapability CreateCapabilityOffer(
            TransportCapabilitySpec spec,
            TransportRequestMessage request,
            string requesterId,
            DateTime scheduleAnchor,
            int placementIndex,
            double legDistance,
            double legCost)
        {
            var offer = new OfferedCapability($"Capability_{placementIndex:D2}");
            var instanceId = $"{spec.ModuleId}-{spec.CapabilityIdShort}-{Guid.NewGuid():N}";
            offer.InstanceIdentifier.Value = new PropertyValue<string>(instanceId);
            offer.Station.Value = new PropertyValue<string>(spec.ModuleId);
            offer.MatchingScore.Value = new PropertyValue<double>(1.0);
            offer.SetSequencePlacement($"LEG_{placementIndex:D2}");
            offer.SetCost(Math.Max(0, legCost));

            var start = scheduleAnchor.AddSeconds(Math.Max(0, (placementIndex - 1) * 5));
            var end = start.AddSeconds(10);
            offer.SetEarliestScheduling(start, end, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));

            if (CapabilityReferenceFactory.TryParseNeo4jReferenceJson(spec.ReferenceJson, out var reference))
            {
                offer.OfferedCapabilityReference.Value = new ReferenceElementValue(reference);
            }

            var action = new ActionModel(
                idShort: $"Action_{spec.CapabilityIdShort}",
                actionTitle: spec.CapabilityIdShort,
                status: ActionStatusEnum.PLANNED,
                inputParameters: new InputParameters(),
                finalResultData: null,
                preconditions: null,
                skillReference: null,
                machineName: spec.ModuleId);

            var inputs = action.InputParameters;
            inputs.SetParameter("RequesterId", requesterId ?? string.Empty);
            inputs.SetParameter("ModuleId", spec.ModuleId);
            inputs.SetParameter("CapabilityIdShort", spec.CapabilityIdShort);
            inputs.SetParameter("CapabilitiesSequenceIndex", placementIndex.ToString(CultureInfo.InvariantCulture));
            inputs.SetParameter("TransportStart", request.TransportStartStationText ?? string.Empty);
            inputs.SetParameter("TransportGoal", request.TransportGoalStationText ?? string.Empty);
            inputs.SetParameter("IdentifierKey", request.IdentifierKeyText ?? string.Empty);
            inputs.SetParameter("Amount", request.AmountValue.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(request.IdentifierTypeText))
            {
                var identifierKey = reqKey(request.IdentifierTypeText);
                if (!string.IsNullOrWhiteSpace(identifierKey))
                {
                    inputs.SetParameter(identifierKey, request.IdentifierValueText ?? string.Empty);
                }
            }

            inputs.SetParameter("start", request.TransportStartStationText ?? string.Empty);
            inputs.SetParameter("ziel", request.TransportGoalStationText ?? string.Empty);

            if (legDistance > 0)
            {
                inputs.SetParameter("LegDistance", legDistance.ToString(CultureInfo.InvariantCulture));
            }

            if (legCost > 0)
            {
                inputs.SetParameter("LegCost", legCost.ToString(CultureInfo.InvariantCulture));
            }

            if (spec.Properties != null)
            {
                foreach (var property in spec.Properties)
                {
                    var key = NormalizeInputKey(property.Key);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    inputs.SetParameter(key, property.Value ?? string.Empty);
                }
            }

            offer.AddAction(action);
            return offer;
        }

        private static (double Distance, double Cost) DetermineCapabilityMetrics(
            TransportCapabilityLeg leg,
            Queue<TransportLeg> transportLegs)
        {
            if (leg == null || transportLegs == null)
            {
                return (0d, 0d);
            }

            if (!string.Equals(leg.CapabilityIdShort, "Transport", StringComparison.OrdinalIgnoreCase))
            {
                return (0d, 0d);
            }

            if (transportLegs.Count == 0)
            {
                return (0d, 0d);
            }

            var planLeg = transportLegs.Dequeue();
            return (planLeg.Distance, planLeg.Cost);
        }

        private string? GetConfigString(params string[] pathSegments)
        {
            if (pathSegments == null || pathSegments.Length == 0)
            {
                return null;
            }

            var dotted = $"config.{string.Join('.', pathSegments)}";
            var direct = Context.Get<object>(dotted);
            var text = JsonFacade.ToStringValue(direct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var configRoot = Context.Get<object>("config");
            return configRoot == null ? null : JsonFacade.GetPathAsString(configRoot, pathSegments);
        }

        private double GetConfigDouble(double fallback, params string[] pathSegments)
        {
            if (pathSegments == null || pathSegments.Length == 0)
            {
                return fallback;
            }

            var dotted = $"config.{string.Join('.', pathSegments)}";
            var directValue = Context.Get<object>(dotted);
            if (JsonFacade.TryToDouble(directValue, out var parsed))
            {
                return parsed;
            }

            var configRoot = Context.Get<object>("config");
            if (configRoot != null)
            {
                var node = JsonFacade.GetPath(configRoot, pathSegments);
                if (JsonFacade.TryToDouble(node, out parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }

        private static string NormalizeInputKey(string? rawKey)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                return string.Empty;
            }

            return rawKey
                .Replace(" ", string.Empty)
                .Replace(":", string.Empty)
                .Replace("/", string.Empty);
        }

        private TransportRequestMessage BuildResponseElement(I40Message incoming, string requesterId,out OfferedCapability transportOffer)
        {
            // Try to find an existing SubmodelElementCollection in the incoming message to construct the request from
            SubmodelElementCollection? incomingCollection = null;
            if (incoming.InteractionElements != null)
            {
                // Prefer the first element if it already is a SubmodelElementCollection (common case)
                incomingCollection = incoming.InteractionElements.FirstOrDefault() as SubmodelElementCollection
                                     ?? incoming.InteractionElements.OfType<SubmodelElementCollection>().FirstOrDefault();
            }

            TransportRequestMessage req;
            if (incomingCollection != null)
            {
                req = new TransportRequestMessage(incomingCollection);
            }
            else
            {
                // fallback: construct from extracted values
                var goal = ExtractValue(incoming, "TransportGoalStation") ?? "Unknown";
                var identifierValue = ExtractValue(incoming, "IdentifierValue") ??  "Unknown";
                var identifierType = ExtractValue(incoming, "IdentifierType") ?? "Unknown";
                var amount = ExtractValue(incoming, "Amount") ?? "1";

                req = new TransportRequestMessage("TransportRequest");
                // Instance identifier - use a generated id
                req.InstanceIdentifier.Value = new PropertyValue<string>($"transportreq_{Guid.NewGuid():N}");
                req.TransportStartStation.Value = new PropertyValue<string>("Storage");
                req.TransportGoalStation.Value = new PropertyValue<string>(goal);
                req.IdentifierType.Value = new PropertyValue<string>(identifierType);
                req.IdentifierValue.Value = new PropertyValue<string>(identifierValue);
                if (int.TryParse(amount, out var amt)) req.SetAmount(amt);
            }

            // Ensure InstanceIdentifier exists
            if (req.InstanceIdentifier.IsNullOrWhiteSpace())
            {
                req.InstanceIdentifier.Value = new PropertyValue<string>($"transportreq_{Guid.NewGuid():N}");
            }

            // Build transport offer based on the resolved goal/identifier/type
            var resolvedGoal = req.TransportGoalStationText ?? "Unknown";
            var resolvedIdentifierValue = req.IdentifierValueText ?? "Unknown";
            var resolvedIdentifierType = req.IdentifierTypeText ?? "Unknown";

            transportOffer = BuildTransportOffer(resolvedGoal, resolvedIdentifierValue, resolvedIdentifierType);
            req.CapabilitiesSequence.AddCapability(transportOffer);

            try { Console.WriteLine($"[DEBUG] HandleTransportPlanRequest: built transport offer id={transportOffer.InstanceIdentifier.GetText()} identifierKey={reqKey(resolvedIdentifierType)} identifierValue={resolvedIdentifierValue}"); } catch {}

            return req;
        }

        private TransportRequestMessage BuildResponseElement(TransportRequestMessage incomingRequest, string requesterId, out OfferedCapability transportOffer)
        {
            var req = incomingRequest ?? new TransportRequestMessage("TransportRequest");

            // Ensure InstanceIdentifier exists
            if (req.InstanceIdentifier.IsNullOrWhiteSpace())
            {
                req.InstanceIdentifier.Value = new PropertyValue<string>($"transportreq_{Guid.NewGuid():N}");
            }

            var resolvedGoal = req.TransportGoalStationText ?? "Unknown";
            var resolvedIdentifierValue = req.IdentifierValueText ?? "Unknown";
            var resolvedIdentifierType = req.IdentifierTypeText ?? "Unknown";

            transportOffer = BuildTransportOffer(resolvedGoal, resolvedIdentifierValue, resolvedIdentifierType);

            // Avoid duplicating offers if already present
            req.CapabilitiesSequence.AddCapability(transportOffer);

            return req;
        }

        private OfferedCapability BuildTransportOffer(string goal, string identifierValue, string identifierType)
        {
            var offer = new OfferedCapability(string.Empty);
            var offerId = $"transport_{Guid.NewGuid():N}";
            offer.InstanceIdentifier.Value = new PropertyValue<string>(offerId);
            offer.Station.Value = new PropertyValue<string>(string.IsNullOrWhiteSpace(goal) ? "Storage" : goal);
            offer.MatchingScore.Value = new PropertyValue<double>(1.0);
            offer.SetEarliestScheduling(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1), TimeSpan.Zero, TimeSpan.FromMinutes(1));
            offer.SetCost(0);

            var reference = new Reference(new[]
            {
                new Key(KeyType.GlobalReference, "Transport")
            })
            {
                Type = ReferenceType.ExternalReference
            };
            offer.OfferedCapabilityReference.Value = new ReferenceElementValue(reference);

            var action = new ActionModel(
                idShort: "Action_Transport",
                actionTitle: "Transport",
                status: ActionStatusEnum.OPEN,
                inputParameters: new InputParameters(),
                finalResultData: null,
                preconditions: null,
                skillReference: null,
                machineName: goal ?? "Transport");

            // Response requirement: return the extracted identifier as input parameter.
            // IMPORTANT: InputParameters keys become AAS idShorts; keep them valid (no ':' / spaces / URLs).
            // Therefore use Key='ProductID' (etc.) and store the identifier in the value.
            var identifierKey = reqKey(identifierType);
            action.InputParameters.SetParameter(identifierKey, identifierValue);
            // Also set the transport target (ziel) for convenience
            try { action.InputParameters.SetParameter("ziel", goal ?? string.Empty); } catch { }
            offer.AddAction(action);

            return offer;
        }

        private static string reqKey(string identifierType)
        {
            if (string.IsNullOrWhiteSpace(identifierType)) return "Identifier";
            if (identifierType.EndsWith("Id", StringComparison.Ordinal) && identifierType.Length > 2)
            {
                return identifierType[..^2] + "ID";
            }
            return identifierType;
        }

        private static Property<string> CreateStringProperty(string idShort, string value)
        {
            return new Property<string>(idShort)
            {
                Value = new PropertyValue<string>(value ?? string.Empty)
            };
        }

        private static string? ExtractValue(I40Message? message, string idShort)
        {
            if (message?.InteractionElements == null)
            {
                return null;
            }

            foreach (var element in message.InteractionElements)
            {
                if (element is SubmodelElementCollection collection)
                {
                    var prop = collection.FirstOrDefault(e => string.Equals(e.IdShort, idShort, StringComparison.OrdinalIgnoreCase)) as Property<string>;
                    var data = prop.GetText();
                    if (!string.IsNullOrWhiteSpace(data))
                    {
                        return data;
                    }
                }
            }

            return null;
        }

        private static string UnwrapValueFromElement(ISubmodelElement element)
        {
            if (element == null) return "<null>";

            // If it's a Property, try to extract nested values
            if (element is Property prop)
            {
                try
                {
                    return AasValueUnwrap.UnwrapToString(prop.Value) ?? "<null>";
                }
                catch
                {
                    return "<error>";
                }
            }

            // For other element types, fallback to ToString()
            return element.ToString() ?? "<null>";
        }

        private void EnsureTransportStartStation(TransportRequestMessage message, I40Message incomingMessage)
        {
            if (message == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(message.TransportStartStationText))
            {
                return;
            }

            var identifierType = message.IdentifierTypeText ?? string.Empty;
            if (!string.Equals(identifierType, TransportRequestMessage.IdentifierTypeEnum.ProductId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var productId = ResolveProductId(message, incomingMessage);
                if (string.IsNullOrWhiteSpace(productId))
                {
                    Console.WriteLine("[DEBUG] EnsureTransportStartStation: productId empty");
                    return;
                }

                Console.WriteLine($"[DEBUG] EnsureTransportStartStation: resolving productId={productId}");
                var sequence = GetManufacturingSequenceForProduct(productId);
                Console.WriteLine($"[DEBUG] EnsureTransportStartStation: sequence_found={(sequence!=null)}");
                if (sequence == null)
                {
                    Logger.LogWarning("HandleTransportPlanRequest: no manufacturing sequence available for product {ProductId}", productId);
                    return;
                }

                var machineName = FindLatestStorageMachine(sequence);
                Console.WriteLine($"[DEBUG] EnsureTransportStartStation: found machine='{machineName}'");
                if (string.IsNullOrWhiteSpace(machineName))
                {
                    Logger.LogWarning("HandleTransportPlanRequest: no storage postcondition found for product {ProductId}", productId);
                    return;
                }

                message.TransportStartStation.Value = new PropertyValue<string>(machineName);
                Logger.LogInformation("HandleTransportPlanRequest: resolved transport start station '{Station}' for product {ProductId}", machineName, productId);
        }

        private static string? ResolveProductId(TransportRequestMessage message, I40Message incomingMessage)
        {
            if (!string.IsNullOrWhiteSpace(message?.IdentifierValueText))
            {
                return message.IdentifierValueText;
            }

            return incomingMessage?.Frame?.ConversationId;
        }

        private ManufacturingSequence? GetManufacturingSequenceForProduct(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return null;

            Dictionary<string, ManufacturingSequence>? index = null;
            try
            {
                index = Context.Get<Dictionary<string, ManufacturingSequence>>("ManufacturingSequence.ByProduct");
            }
            catch
            {
                // Key not present yet.
            }

            if (index != null)
            {
                // direct lookup
                if (index.TryGetValue(productId, out var sequence) && sequence != null)
                {
                    return sequence;
                }

                // heuristic: try trimming trailing underscores or common suffixes
                var normalized = productId.TrimEnd('_');
                if (!string.Equals(normalized, productId, StringComparison.Ordinal) && index.TryGetValue(normalized, out sequence) && sequence != null)
                {
                    return sequence;
                }

                // heuristic: try last path segment
                try
                {
                    var last = productId.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    if (!string.IsNullOrWhiteSpace(last))
                    {
                        if (index.TryGetValue(last, out sequence) && sequence != null) return sequence;

                        // also try variations
                        var lastNorm = last.TrimEnd('_');
                        if (index.TryGetValue(lastNorm, out sequence) && sequence != null) return sequence;
                    }
                }
                catch { }

                // fallback: try contains match
                foreach (var kv in index)
                {
                    if (kv.Key != null && (kv.Key.IndexOf(productId, StringComparison.OrdinalIgnoreCase) >= 0 || productId.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return kv.Value;
                    }
                }
            }

            return null;
        }

        private static string? FindLatestStorageMachine(ManufacturingSequence sequence)
        {
            if (sequence == null)
            {
                return null;
            }

            var requirements = sequence.GetRequiredCapabilities().ToList();
            for (var reqIndex = requirements.Count - 1; reqIndex >= 0; reqIndex--)
            {
                var requirement = requirements[reqIndex];
                if (requirement == null)
                {
                    continue;
                }

                var sequences = requirement.GetSequences().ToList();
                for (var seqIndex = sequences.Count - 1; seqIndex >= 0; seqIndex--)
                {
                    var offeredSequence = sequences[seqIndex];
                    if (offeredSequence == null)
                    {
                        continue;
                    }

                    var capabilities = offeredSequence.GetCapabilities().ToList();
                    for (var capIndex = capabilities.Count - 1; capIndex >= 0; capIndex--)
                    {
                        var capability = capabilities[capIndex];
                        var machineName = TryFindStorageMachine(capability);
                        if (!string.IsNullOrWhiteSpace(machineName))
                        {
                            return machineName;
                        }
                    }
                }
            }

            return null;
        }

        private static string? TryFindStorageMachine(OfferedCapability capability)
        {
            if (capability?.Actions == null)
            {
                return null;
            }

            var actions = capability.Actions.OfType<ActionModel>().ToList();
            for (var actionIndex = actions.Count - 1; actionIndex >= 0; actionIndex--)
            {
                var action = actions[actionIndex];
                if (action == null)
                {
                    continue;
                }

                if (HasStoragePostcondition(action))
                {
                    return action.MachineName.GetText();
                }
            }

            return null;
        }

        private static bool HasStoragePostcondition(ActionModel action)
        {
            if (action?.Postconditions == null)
            {
                return false;
            }

            foreach (var post in action.Postconditions.OfType<StoragePostcondition>())
            {
                return true;
            }

            return false;
        }
    }
}

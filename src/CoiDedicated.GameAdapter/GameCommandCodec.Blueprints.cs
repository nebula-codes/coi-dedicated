using System;
using System.Collections.Generic;
using System.Reflection;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Recipes;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        public const int MaxBlueprintProtoEntries = 64;
        // This schema has fixed keys and fixed types. The wire contains no dictionary keys,
        // game serializer blobs, CLR type names, callbacks, or recursive configuration graphs.
        private static readonly string[] BlueprintInts = {
            "OriginalEntityId", "LogisticsInputMode", "LogisticsOutputMode", "GeneralPriority",
            "ElectricityGenerationPriority", "ConstructionPriorityOverride", "StorageImportPriority", "StorageExportPriority",
            "StorageImportUntilPercent", "StorageExportFromPercent", "StorageTransportFromPercent", "StorageTransportUntilPercent",
            "AlertWhenAbove", "AlertWhenBelow" };
        private static readonly string[] BlueprintBools = {
            "IsPaused", "IsLogisticsInputDisabled", "IsLogisticsOutputDisabled", "IsElectricitySurplusGenerator",
            "IsElectricitySurplusConsumer", "AllowNonAssignedOutput", "AlertWhenAboveEnabled", "AlertWhenBelowEnabled", "IsEnforcingCustomVehicles" };
        private static readonly FieldInfo? BlueprintDataField = typeof(EntityConfigData).GetField("m_data", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? BlueprintTransientField = typeof(EntityConfigData).GetField("m_nonPersistentData", BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class BlueprintSettings
        {
            internal int?[] Ints = new int?[BlueprintInts.Length];
            internal bool?[] Bools = new bool?[BlueprintBools.Length];
            internal bool? ConstructionPaused;
            internal string? Title, Product;
            internal string[]? Recipes, TruckGroups;
        }

        private static void EncodeBlueprintBatch(BatchCreateStaticEntitiesCmd command, PayloadWriter writer)
        {
            if (command.IsFree || command.AllowValidationSuppression) throw new ProtocolException("Blueprints require normal placement and construction costs.");
            ValidateBatchCount(command.ConfigData.Length); ValidateMiniZippers(command.BuildMiniZippers);
            writer.WriteUInt16(400); writer.WriteUInt16((ushort)command.BuildMiniZippers); writer.WriteUInt16((ushort)command.ConfigData.Length);
            var originalIds = new HashSet<int>();
            foreach (EntityConfigData data in command.ConfigData)
            {
                if (data?.Prototype.ValueOrNull is not LayoutEntityProto proto || !data.Transform.HasValue)
                    throw new ProtocolException("Blueprint building prototype or transform is missing.");
                RequireBlueprintFamily(proto);
                ValidateProtoId(proto.Id.Value); ValidatePlacementScalars(data.Transform.Value.Position, data.Transform.Value.Rotation.AngleIndex);
                BlueprintSettings settings = CaptureBlueprintSettings(data);
                ValidateBlueprintSettings(settings, proto is StorageProto);
                if (settings.Ints[0].HasValue && !originalIds.Add(settings.Ints[0]!.Value)) throw new ProtocolException("Duplicate blueprint source entity ID.");
                writer.WriteString(proto.Id.Value, MaxProtoIdBytes); WriteBuildingTransform(writer, data.Transform.Value);
                WriteBlueprintSettings(writer, settings);
            }
        }

        private static BatchCreateStaticEntitiesCmd DecodeBlueprintBatch(PayloadReader reader, DependencyResolver resolver)
        {
            var mode = (BuildMiniZippersMode)reader.ReadUInt16(); ValidateMiniZippers(mode);
            int count = reader.ReadUInt16(); ValidateBatchCount(count);
            var ids = new string[count]; var transforms = new TileTransform[count]; var settings = new BlueprintSettings[count];
            var sourceIds = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                ids[i] = ReadScalarProtoId(reader); transforms[i] = ReadBuildingTransform(reader); settings[i] = ReadBlueprintSettings(reader);
                if (settings[i].Ints[0].HasValue && !sourceIds.Add(settings[i].Ints[0]!.Value)) throw new ProtocolException("Duplicate blueprint source entity ID.");
            }
            reader.EnsureEnd();
            // Validate the full bounded request before creating any native config or command.
            var protos = new LayoutEntityProto[count];
            for (int i = 0; i < count; i++)
            {
                protos[i] = RequireBuildableLayout(resolver, ids[i], transforms[i]); RequireBlueprintFamily(protos[i]);
                ValidateBlueprintSettings(settings[i], protos[i] is StorageProto);
                ValidateBlueprintReferences(settings[i], protos[i], resolver);
            }
            var configs = new ImmutableArrayBuilder<EntityConfigData>(count);
            var context = resolver.Resolve<ConfigSerializationContext>();
            for (int i = 0; i < count; i++)
            {
                var data = new EntityConfigData(protos[i], context) { Transform = transforms[i] };
                ApplyBlueprintSettings(data, settings[i], resolver); configs[i] = data;
            }
            return new BatchCreateStaticEntitiesCmd(configs.GetImmutableArrayAndClear(), mode,
                isFree: false, allowValidationSuppression: false, applyConfiguration: true);
        }

        private static void RequireBlueprintFamily(LayoutEntityProto proto)
        {
            if (proto is not MachineProto && proto is not StorageProto)
                throw new ProtocolException("Copied settings currently support ordinary machines and storage buildings only.");
        }

        private static BlueprintSettings CaptureBlueprintSettings(EntityConfigData data)
        {
            object raw = BlueprintDataField?.GetValue(data) ?? throw new ProtocolException("Game configuration field layout changed.");
            Dict<string, T> Dictionary<T>(string field) => raw.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(raw) as Dict<string, T>
                ?? throw new ProtocolException("Game configuration dictionary layout changed.");
            void Known<T>(Dict<string, T> dictionary, params string[] keys)
            {
                var allowed = new HashSet<string>(keys, StringComparer.Ordinal);
                foreach (var pair in dictionary) if (!allowed.Contains(pair.Key))
                    throw new ProtocolException($"Copied setting '{pair.Key}' is not supported yet; copy without settings for this building.");
            }
            Known(Dictionary<int>("Integers"), BlueprintInts); Known(Dictionary<bool>("Booleans"), BlueprintBools);
            Known(Dictionary<string>("Strings"), "Prototype", "CustomTitle", "StorageStoredProduct");
            var arrays = Dictionary<ImmutableArray<string>>("StringArrays"); Known(arrays, "Recipes", "AllowedTruckGroups");
            Known(Dictionary<ImmutableArray<byte>>("ByteArrays"), "Transform");
            if (BlueprintTransientField?.GetValue(data) is not Option<Dict<string, ImmutableArray<byte>>> transient)
                throw new ProtocolException("Game transient configuration field layout changed.");
            if (transient.HasValue) Known(transient.Value, "IsConstructionPaused");
            var result = new BlueprintSettings { ConstructionPaused = data.IsConstructionPaused,
                Title = data.CustomTitle.ValueOrNull, Product = data.GetString("StorageStoredProduct").ValueOrNull };
            for (int i = 0; i < BlueprintInts.Length; i++) result.Ints[i] = data.GetInt(BlueprintInts[i]);
            for (int i = 0; i < BlueprintBools.Length; i++) result.Bools[i] = data.GetBool(BlueprintBools[i]);
            string[]? Copy(string key)
            {
                if (!arrays.TryGetValue(key, out var values)) return null;
                ValidateRange(values.Length, 0, MaxBlueprintProtoEntries, "Blueprint prototype entries");
                return values.ToArray();
            }
            result.Recipes = Copy("Recipes"); result.TruckGroups = Copy("AllowedTruckGroups");
            return result;
        }

        private static void ValidateBlueprintSettings(BlueprintSettings s, bool? storage)
        {
            for (int i = 0; i < s.Ints.Length; i++) if (s.Ints[i].HasValue)
            {
                int n = s.Ints[i]!.Value;
                if (i == 0) ValidateEntityId(n);
                else if (i <= 2) ValidateRange(n, 0, 2, "Blueprint logistics mode");
                else if (i <= 7) ValidatePriority(n);
                else ValidateRange(n, 0, Percent.Hundred.RawValue, "Blueprint percentage");
                if (storage == false && i >= 6) throw new ProtocolException("Storage settings require a storage building.");
            }
            if (storage == false && (s.Product != null || s.TruckGroups != null || s.Bools[6].HasValue || s.Bools[7].HasValue || s.Bools[8].HasValue))
                throw new ProtocolException("Storage settings require a storage building.");
            if (storage == true && s.Recipes != null) throw new ProtocolException("Recipe settings require a machine.");
            if (s.Title != null) ValidateEntityTitle(s.Title);
            if (s.Product != null) ValidateProtoId(s.Product);
            foreach (var values in new[] { s.Recipes, s.TruckGroups }) if (values != null)
            {
                ValidateRange(values.Length, 0, MaxBlueprintProtoEntries, "Blueprint prototype entries");
                var unique = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in values) { ValidateProtoId(id); if (!unique.Add(id)) throw new ProtocolException("Duplicate blueprint prototype entry."); }
            }
        }

        private static void WriteBlueprintSettings(PayloadWriter writer, BlueprintSettings s)
        {
            foreach (int? n in s.Ints) { writer.WriteBoolean(n.HasValue); if (n.HasValue) writer.WriteInt32(n.Value); }
            foreach (bool? b in s.Bools) { writer.WriteBoolean(b.HasValue); if (b.HasValue) writer.WriteBoolean(b.Value); }
            writer.WriteBoolean(s.ConstructionPaused.HasValue); if (s.ConstructionPaused.HasValue) writer.WriteBoolean(s.ConstructionPaused.Value);
            writer.WriteBoolean(s.Title != null); if (s.Title != null) writer.WriteString(s.Title, 256);
            WriteOptionalProto(writer, s.Product);
            foreach (string[]? array in new[] { s.Recipes, s.TruckGroups })
            {
                writer.WriteBoolean(array != null); if (array == null) continue;
                writer.WriteUInt16((ushort)array.Length); foreach (string id in array) writer.WriteString(id, MaxProtoIdBytes);
            }
        }

        private static BlueprintSettings ReadBlueprintSettings(PayloadReader reader)
        {
            var s = new BlueprintSettings();
            for (int i = 0; i < s.Ints.Length; i++) if (reader.ReadBoolean()) s.Ints[i] = reader.ReadInt32();
            for (int i = 0; i < s.Bools.Length; i++) if (reader.ReadBoolean()) s.Bools[i] = reader.ReadBoolean();
            if (reader.ReadBoolean()) s.ConstructionPaused = reader.ReadBoolean();
            if (reader.ReadBoolean()) s.Title = reader.ReadString(256);
            s.Product = ReadOptionalProto(reader);
            string[]? Array()
            {
                if (!reader.ReadBoolean()) return null;
                int count = reader.ReadUInt16(); ValidateRange(count, 0, MaxBlueprintProtoEntries, "Blueprint prototype entries");
                var values = new string[count]; for (int i = 0; i < count; i++) values[i] = ReadScalarProtoId(reader); return values;
            }
            s.Recipes = Array(); s.TruckGroups = Array(); ValidateBlueprintSettings(s, null); return s;
        }

        private static void ValidateBlueprintReferences(BlueprintSettings s, LayoutEntityProto proto, DependencyResolver resolver)
        {
            if (s.Product != null) RequireResearchTradeProto<ProductProto>(resolver, s.Product);
            if (s.Recipes != null) foreach (string id in s.Recipes)
            {
                var recipe = RequireResearchTradeProto<RecipeProto>(resolver, id);
                if (recipe.IsLockedOrUnavailable || proto is not MachineProto machine || !machine.Recipes.Contains(recipe))
                    throw new ProtocolException("Blueprint recipe is locked or incompatible with this machine.");
            }
            if (s.TruckGroups != null) foreach (string id in s.TruckGroups)
            {
                var group = RequireResearchTradeProto<VehicleGroupProto>(resolver, id);
                if (group.IsLockedOrUnavailable) throw new ProtocolException("Blueprint truck group is locked.");
            }
        }

        private static void ApplyBlueprintSettings(EntityConfigData data, BlueprintSettings s, DependencyResolver resolver)
        {
            for (int i = 0; i < s.Ints.Length; i++) data.SetInt(BlueprintInts[i], s.Ints[i]);
            for (int i = 0; i < s.Bools.Length; i++) data.SetBool(BlueprintBools[i], s.Bools[i]);
            data.IsConstructionPaused = s.ConstructionPaused;
            if (s.Title != null) data.CustomTitle = s.Title;
            if (s.Product != null) data.SetStorageStoredProduct(RequireResearchTradeProto<ProductProto>(resolver, s.Product));
            if (s.Recipes != null)
            {
                var values = new ImmutableArrayBuilder<RecipeProto>(s.Recipes.Length);
                for (int i = 0; i < s.Recipes.Length; i++) values[i] = RequireResearchTradeProto<RecipeProto>(resolver, s.Recipes[i]);
                data.Recipes = values.GetImmutableArrayAndClear();
            }
            if (s.TruckGroups != null)
            {
                var values = new ImmutableArrayBuilder<VehicleGroupProto>(s.TruckGroups.Length);
                for (int i = 0; i < s.TruckGroups.Length; i++) values[i] = RequireResearchTradeProto<VehicleGroupProto>(resolver, s.TruckGroups[i]);
                data.SetProtoArray<VehicleGroupProto>("AllowedTruckGroups", values.GetImmutableArrayAndClear());
            }
        }
    }
}

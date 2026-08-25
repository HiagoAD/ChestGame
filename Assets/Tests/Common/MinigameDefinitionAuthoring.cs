using System;
using System.Reflection;
using Company.ChestGame.Minigame.Core;
using UnityEngine.AddressableAssets;

namespace Company.ChestGame.Tests.Common
{
    // MinigameBaseSO's authored fields are serialized and private, so a definition built with
    // CreateInstance carries empty ones. Tests write them directly, which means no production type
    // has to open a setter it does not otherwise need.
    public static class MinigameDefinitionAuthoring
    {
        private static readonly FieldInfo IdField =
            typeof(MinigameBaseSO).GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic);

        public static TDefinition WithId<TDefinition>(this TDefinition definition, string id)
            where TDefinition : MinigameBaseSO
        {
            // A rename would otherwise surface as a NullReferenceException from a helper nobody
            // suspects.
            if (IdField == null)
            {
                throw new MissingFieldException(
                    $"{nameof(MinigameBaseSO)} no longer has a '_id' field; MinigameDefinitionAuthoring needs updating");
            }

            IdField.SetValue(definition, id);
            return definition;
        }

        // The two fields the delivery work reads, authored together because they are one decision:
        // a label with no policy names content nothing will fetch.
        public static TDefinition WithContent<TDefinition>(
            this TDefinition definition, string contentLabel, MinigameLoadPolicy loadPolicy)
            where TDefinition : MinigameBaseSO
        {
            Set(definition, "_contentLabel", contentLabel);
            Set(definition, "_loadPolicy", loadPolicy);

            return definition;
        }

        private static void Set(MinigameBaseSO definition, string fieldName, object value)
        {
            FieldInfo field = typeof(MinigameBaseSO).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                throw new MissingFieldException(
                    $"{nameof(MinigameBaseSO)} no longer has a '{fieldName}' field; MinigameDefinitionAuthoring needs updating");
            }

            field.SetValue(definition, value);
        }

        // The view reference lives on the generic base, so the field is found by walking up from
        // the concrete definition's own type.
        public static TDefinition WithViewReference<TDefinition>(this TDefinition definition, AssetReferenceGameObject viewRef)
            where TDefinition : MinigameBaseSO
        {
            FieldInfo field = DeclaredField(definition.GetType(), "_viewRef");
            if (field == null)
            {
                throw new MissingFieldException(
                    $"{definition.GetType().Name} no longer has a '_viewRef' field; MinigameDefinitionAuthoring needs updating");
            }

            field.SetValue(definition, viewRef);
            return definition;
        }

        private static FieldInfo DeclaredField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (field != null) return field;
            }

            return null;
        }
    }
}

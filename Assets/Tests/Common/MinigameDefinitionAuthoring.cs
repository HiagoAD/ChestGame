using System;
using System.Reflection;
using Company.ChestGame.Minigame.Core;
using UnityEngine.AddressableAssets;

namespace Company.ChestGame.Tests.Common
{
    /// <summary>
    /// Writes <see cref="MinigameBaseSO"/>'s private serialized authoring fields directly, for a
    /// definition built with <c>CreateInstance</c> that needs them populated for a test.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "MinigameDefinitionAuthoring, and why it reaches through reflection".
    /// </remarks>
    public static class MinigameDefinitionAuthoring
    {
        private static readonly FieldInfo IdField =
            typeof(MinigameBaseSO).GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Sets <see cref="MinigameBaseSO"/>'s private <c>_id</c> field on
        /// <paramref name="definition"/>.
        /// </summary>
        /// <exception cref="MissingFieldException">
        /// When <see cref="MinigameBaseSO"/> no longer has an <c>_id</c> field.
        /// </exception>
        public static TDefinition WithId<TDefinition>(this TDefinition definition, string id)
            where TDefinition : MinigameBaseSO
        {
            if (IdField == null)
            {
                throw new MissingFieldException(
                    $"{nameof(MinigameBaseSO)} no longer has a '_id' field; MinigameDefinitionAuthoring needs updating");
            }

            IdField.SetValue(definition, id);
            return definition;
        }

        /// <summary>
        /// Sets the content label and load policy on <paramref name="definition"/> together.
        /// </summary>
        /// <exception cref="MissingFieldException">
        /// When <see cref="MinigameBaseSO"/> no longer has the backing field for either value.
        /// </exception>
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

        /// <summary>
        /// Sets <paramref name="viewRef"/> on the <c>_viewRef</c> field declared by the generic
        /// <c>MinigameBase&lt;TController, TView, TMinigame&gt;</c> base, found by walking up from
        /// <paramref name="definition"/>'s own type.
        /// </summary>
        /// <exception cref="MissingFieldException">
        /// When no type in the hierarchy still declares a <c>_viewRef</c> field.
        /// </exception>
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

﻿// Copyright (c) 2018 Jon P Smith, GitHub: JonPSmith, web: http://www.thereformedprogrammer.net/
// Licensed under MIT licence. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using GenericServices.Configuration;

namespace GenericServices.Internal.Decoders
{
    internal class DecodedDto : StatusGenericHandler
    {
        private readonly IGenericServiceConfig _overallConfig;
        private readonly List<MethodInfo> _availableSetterMethods = new List<MethodInfo>();

        public Type DtoType { get; }
        public Type LinkedToType { get; }
        public ImmutableList<DecodedDtoProperty> PropertyInfos { get; }

        /// <summary>
        /// This contains the update methods that are available to the user - if there is more than one the user must define which one they want
        /// </summary>
        public ImmutableList<MethodMatch> MatchedSetterMethods { get; }

        public DecodedDto(Type dtoType, DecodedEntityClass entityInfo, 
            IGenericServiceConfig overallConfig, PerDtoConfig perDtoConfig)
        {
            DtoType = dtoType ?? throw new ArgumentNullException(nameof(dtoType));
            _overallConfig = overallConfig ?? throw new ArgumentNullException(nameof(overallConfig));
            LinkedToType = entityInfo.EntityType;
            PropertyInfos = dtoType.GetProperties()
                .Select(x => new DecodedDtoProperty(x, 
                        BestPropertyMatch.FindMatch(x, entityInfo.PrimaryKeyProperties ).Score >= PropertyMatch.PerfectMatchValue))
                .ToImmutableList();

            if (entityInfo.CanBeUpdatedViaMethods || perDtoConfig?.UpdateMethods != null)
                MatchedSetterMethods = MatchUpdateMethods(entityInfo, perDtoConfig?.UpdateMethods).ToImmutableList();
        }

        private List<MethodMatch> MatchUpdateMethods(DecodedEntityClass entityInfo, string updateMethods)
        {
            var nonReadOnlyPropertyInfo = PropertyInfos.Where(y => y.PropertyType != DtoPropertyTypes.ReadOnly)
                .Select(x => x.PropertyInfo).ToList();

            var result = new List<MethodMatch>();
            if (updateMethods != null)
            {          
                //The user has defined the exact update methods they want matched
                foreach (var methodName in updateMethods.Split(',').Select(x => x.Trim()))
                {
                    var matches = MethodMatch.GradeAllMethods(FindMethodsWithGivenName(entityInfo, methodName, true).ToArray(),
                         nonReadOnlyPropertyInfo, HowTheyWereAskedFor.SpecifiedInPerDtoConfig, _overallConfig.NameMatcher);
                    var firstMatch = matches.FirstOrDefault();
                    if (firstMatch == null || firstMatch.PropertiesMatch.Score < PropertyMatch.PerfectMatchValue)
                        AddError(
                            $"You asked for update method {methodName}, but could not find a exact match of parameters." +
                            (firstMatch == null ? "" : $" Closest fit is {firstMatch}."));
                    else
                    {
                        result.Add(firstMatch);
                    }
                }
            }
            else
            {
                //The developer hasn't defined want methods should be mapped, so we take a guess based on the DTO's name
                var methodNameToLookFor = ExtractPossibleMethodNameFromDtoTypeName();
                var methodsThatMatchedDtoName = FindMethodsWithGivenName(entityInfo, methodNameToLookFor, false);
                if (methodsThatMatchedDtoName.Any())
                {
                    var matches = MethodMatch.GradeAllMethods(methodsThatMatchedDtoName.ToArray(),
                        nonReadOnlyPropertyInfo, HowTheyWereAskedFor.NamedMethodFromDtoClass, _overallConfig.NameMatcher);
                    var firstMatch = matches.FirstOrDefault();
                    if (firstMatch != null || firstMatch.PropertiesMatch.Score >= PropertyMatch.PerfectMatchValue)
                        result.Add(firstMatch);
                }
                if (!result.Any())
                {
                    //Nothing else has worked so do a default scan of all methods
                    var matches = MethodMatch.GradeAllMethods(methodsThatMatchedDtoName.ToArray(),
                        nonReadOnlyPropertyInfo, HowTheyWereAskedFor.DefaultMatchToProperties, _overallConfig.NameMatcher);
                    result.AddRange(matches.Where(x => x.PropertiesMatch.Score >= PropertyMatch.PerfectMatchValue));
                }
            }

            return result;
        }

        private List<MethodInfo> FindMethodsWithGivenName(DecodedEntityClass entityInfo, string methodName, bool raiseErrorIfNotThere)
        {
            var foundMethods = new List<MethodInfo>();
            var methodsToMatch = entityInfo.PublicSetterMethods
                .Where(x => x.Name == methodName).ToList();
            if (!methodsToMatch.Any() && raiseErrorIfNotThere)
            {
                AddError(
                    $"In the PerDtoConfig you asked for the method {methodName}," +
                    $" but that wasn't found in entity class {entityInfo.EntityType.GetNameForClass()}.");
            }
            else
            {
                foundMethods.AddRange(methodsToMatch);
            }

            return foundMethods;
        }

        private readonly string[] _endingsToRemove = new[] {"Dto", "VM", "ViewModel"};

        private string ExtractPossibleMethodNameFromDtoTypeName()
        {
            var name = DtoType.Name;
            foreach (var ending in _endingsToRemove)
            {
                if (name.EndsWith(ending, StringComparison.InvariantCultureIgnoreCase))
                    return name.Substring(0, name.Length - ending.Length);
            }

            return name;
        }
    }
}
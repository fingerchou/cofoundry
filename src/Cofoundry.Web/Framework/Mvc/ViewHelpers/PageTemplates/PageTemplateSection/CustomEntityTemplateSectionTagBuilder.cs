﻿using Conditions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Cofoundry.Domain;
using Cofoundry.Core.Web;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel;
using System.IO;
using System.Text.Encodings.Web;

namespace Cofoundry.Web
{
    /// <summary>
    /// Fluent builder for defining a page template custom entity section configuration using method chaining
    /// </summary>
    public class CustomEntityTemplateSectionTagBuilder<TModel> : ICustomEntityTemplateSectionTagBuilder<TModel>
        where TModel : ICustomEntityDetailsDisplayViewModel 
    {
        #region constructor

        const string DEFAULT_TAG ="div";
        private readonly ICustomEntityDetailsPageViewModel<TModel> _customEntityViewModel;
        private readonly ViewContext _viewContext;
        private readonly IPageModuleRenderer _moduleRenderer;
        private readonly IPageModuleDataModelTypeFactory _moduleDataModelTypeFactory;
        private readonly IPageModuleTypeFileNameFormatter _moduleTypeFileNameFormatter;
        private readonly List<string> _allowedModules = new List<string>();

        public CustomEntityTemplateSectionTagBuilder(
            IPageModuleRenderer moduleRenderer,
            IPageModuleDataModelTypeFactory moduleDataModelTypeFactory,
            IPageModuleTypeFileNameFormatter moduleTypeFileNameFormatter,
            ViewContext viewContext,
            ICustomEntityDetailsPageViewModel<TModel> customEntityViewModel, 
            string sectionName)
        {
            Condition.Requires(sectionName).IsNotNullOrWhiteSpace();
            Condition.Requires(customEntityViewModel).IsNotNull();

            _moduleRenderer = moduleRenderer;
            _moduleDataModelTypeFactory = moduleDataModelTypeFactory;
            _moduleTypeFileNameFormatter = moduleTypeFileNameFormatter;
            _sectionName = sectionName;
            _customEntityViewModel = customEntityViewModel;
            _viewContext = viewContext;
        }

        #endregion

        #region state properties

        private string _output = null;
        private string _sectionName;
        private string _wrappingTagName = null;
        private bool _allowMultipleModules = false;
        private int? _emptyContentMinHeight = null;
        private Dictionary<string, string> _additonalHtmlAttributes = null;
        private Dictionary<string, object> _permittedModules = new Dictionary<string, object>();

        #endregion

        #region public chaining methods

        /// <summary>
        /// By default only a single module is allowed per section. This method configures the
        /// section to allow an unlimited number of modules to be added
        /// </summary>
        /// <returns>IPageTemplateSectionTagBuilder for method chaining</returns>
        public ICustomEntityTemplateSectionTagBuilder<TModel> AllowMultipleModules()
        {
            _allowMultipleModules = true;
            return this;
        }

        /// <summary>
        /// Wraps page modules within the section in a specific html tag. Each section
        /// needs to have a single container node in page edit mode and by default a div
        /// element is used if a single container cannot be found in the rendered output.
        /// Use this setting to control this tag. If a tag is specified here the output
        /// will always be wrapped irrespecitive of whether the page is in edit mode to maintain
        /// consistency.
        /// </summary>
        /// <param name="tagName">Name of the element to wrap the output in e.g. div, p, header</param>
        /// <param name="htmlAttributes">Html attributes to apply to the wrapping tag.</param>
        /// <returns>IPageTemplateSectionTagBuilder for method chaining</returns>
        public ICustomEntityTemplateSectionTagBuilder<TModel> WrapWithTag(string tagName, object htmlAttributes = null)
        {
            Condition.Requires(tagName).IsNotNullOrWhiteSpace();

            _wrappingTagName = tagName;
            _additonalHtmlAttributes = TemplateSectionTagBuilderHelper.ParseHtmlAttributesFromAnonymousObject(htmlAttributes);

            return this;
        }

        /// <summary>
        /// Sets the default height of the section when there it contains no
        /// modules and the page is in edit mode. This makes it easier to visualize
        /// and select the module when a template is first created.
        /// </summary>
        /// <param name="minHeight">The minimum height in pixels</param>
        /// <returns>IPageTemplateSectionTagBuilder for method chaining</returns>
        public ICustomEntityTemplateSectionTagBuilder<TModel> EmptyContentMinHeight(int minHeight)
        {
            _emptyContentMinHeight = minHeight;
            return this;
        }

        /// <summary>
        /// Permits the page module associated with the specified data model type
        /// to be selected and used in this page section. By default all modules are 
        /// available but using this method changes the module selection to an 'opt-in'
        /// configuration.
        /// </summary>
        /// <typeparam name="TModuleType">The data model type of the page module to register</typeparam>
        /// <returns>ICustomEntityTemplateSectionTagBuilder for method chaining</returns>
        public ICustomEntityTemplateSectionTagBuilder<TModel> AllowModuleType<TModuleType>() where TModuleType : IPageModuleDataModel
        {
            AddModuleToAllowedTypes(typeof(TModuleType).Name);
            return this;
        }

        /// <summary>
        /// Permits the specified page module to be selected and used in this page 
        /// section. By default all modules are available but using this method changes 
        /// the module selection to an 'opt-in' configuration.
        /// </summary>
        /// <param name="moduleTypeName">The name of the moduletype to register e.g. "RawHtml" or "PlainText"</param>
        /// <returns>ICustomEntityTemplateSectionTagBuilder for method chaining</returns>
        public ICustomEntityTemplateSectionTagBuilder<TModel> AllowModuleType(string moduleTypeName)
        {
            AddModuleToAllowedTypes(moduleTypeName);
            return this;
        }

        /// <summary>
        /// Permits the specified page modules to be selected and used in this page 
        /// section. By default all modules are available but using this method changes 
        /// the module selection to an 'opt-in' configuration.
        /// configuration.
        /// </summary>
        /// <param name="moduleTypeNames">The names of the moduletype to register e.g. "RawHtml" or "PlainText"</param>
        /// <returns>ICustomEntityTemplateSectionTagBuilder for method chaining</returns>
        public ICustomEntityTemplateSectionTagBuilder<TModel> AllowModuleTypes(params string[] moduleTypeNames)
        {
            foreach (var moduleTypeName in moduleTypeNames)
            {
                AddModuleToAllowedTypes(moduleTypeName);
            }
            return this;
        }

        private void AddModuleToAllowedTypes(string moduleTypeName)
        {
            // Get the file name if we haven't already got it, e.g. we could have the file name or 
            // full type name passed into here (we shouldn't be fussy)
            var fileName = _moduleTypeFileNameFormatter.FormatFromDataModelName(moduleTypeName);

            // Validate the model type, will throw exception if not implemented
            var moduleType = _moduleDataModelTypeFactory.CreateByPageModuleTypeFileName(fileName);

            // Make sure we have the correct name casing
            var formattedModuleTypeName = _moduleTypeFileNameFormatter.FormatFromDataModelType(moduleType);

            _permittedModules.Add(formattedModuleTypeName, null);
        }

        #endregion

        #region rendering

        /// <summary>
        /// This method must be called at the end of the section definition to build and render the
        /// section.
        /// </summary>
        public async Task InvokeAsync()
        {
            var section = _customEntityViewModel
                .CustomEntity
                .Sections
                .SingleOrDefault(s => s.Name == _sectionName);

            if (section != null)
            {
                _output = await RenderSection(section);
            }
            else
            {
                var msg = "WARNING: The custom entity section '" + _sectionName + "' cannot be found in the database";

                Debug.Assert(section != null, msg);
                if (_customEntityViewModel.CustomEntity.WorkFlowStatus != WorkFlowStatus.Published)
                {
                    _output = "<!-- " + msg + " -->";
                }
            }
        }

        public void WriteTo(TextWriter writer, HtmlEncoder encoder)
        {
            Debug.Assert(_output != null, $"Template section '{ _sectionName }' definition does not call { nameof(InvokeAsync)}().");

            if (_output != null)
            {
                writer.Write(_output);
            }
        }

        private async Task<string> RenderSection(CustomEntityPageSectionRenderDetails pageSection)
        {
            string modulesHtml = string.Empty;

            if (pageSection.Modules.Any())
            {
                var renderingTasks = pageSection
                        .Modules
                        .Select(m => _moduleRenderer.RenderModuleAsync(_viewContext, _customEntityViewModel, m));

                var moduleHtmlParts = await Task.WhenAll(renderingTasks);

                if (!_allowMultipleModules)
                {
                    // If for some reason another module has been added in error, make sure we only display one.
                    modulesHtml = moduleHtmlParts.Last();
                }
                else
                {
                    modulesHtml = string.Join(string.Empty, moduleHtmlParts);
                }
            }
            else if (!_allowMultipleModules)
            {
                // If there are no modules and this is a single module section
                // add a placeholder element so we always have a menu
                modulesHtml = _moduleRenderer.RenderPlaceholderModule(_emptyContentMinHeight);
            }

            // If we're not in edit mode just return the modules.
            if (!_customEntityViewModel.IsPageEditMode) // TODO: IsCustom entity mode
            {
                if (_wrappingTagName != null)
                {
                    return TemplateSectionTagBuilderHelper.WrapInTag(
                        modulesHtml,
                        _wrappingTagName,
                        _allowMultipleModules,
                        _additonalHtmlAttributes
                        );
                }

                return modulesHtml;
            }

            var attrs = new Dictionary<string, string>();
            attrs.Add("data-cms-page-template-section-id", pageSection.PageTemplateSectionId.ToString());
            attrs.Add("data-cms-page-section-name", pageSection.Name);
            attrs.Add("data-cms-custom-entity-section", string.Empty);

            attrs.Add("class", "cofoundry__sv-section");

            if (_permittedModules.Any())
            {
                var permittedModules = _permittedModules.Select(m => m.Key);
                attrs.Add("data-cms-page-section-permitted-module-types", string.Join(",", permittedModules));
            }

            if (_allowMultipleModules)
            {
                attrs.Add("data-cms-multi-module", "true");

                if (_emptyContentMinHeight.HasValue)
                {
                    attrs.Add("style", "min-height:" + _emptyContentMinHeight + "px");
                }
            }
            
            return TemplateSectionTagBuilderHelper.WrapInTag(
                modulesHtml,
                _wrappingTagName,
                _allowMultipleModules,
                _additonalHtmlAttributes,
                attrs
                );
        }

        #endregion
    }
}

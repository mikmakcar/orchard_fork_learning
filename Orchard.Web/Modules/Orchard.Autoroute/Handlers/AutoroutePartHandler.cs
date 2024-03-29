﻿using System;
using System.Linq;
using Orchard.ContentManagement;
using Orchard.ContentManagement.Handlers;
using Orchard.Autoroute.Models;
using Orchard.Data;
using Orchard.Autoroute.Services;
using Orchard.Localization;
using Orchard.UI.Notify;
using Orchard.Alias;

namespace Orchard.Autoroute.Handlers {
    public class AutoroutePartHandler : ContentHandler {

        private readonly Lazy<IAutorouteService> _autorouteService;
        private readonly IOrchardServices _orchardServices;
        private readonly IAliasService _aliasService;

        public Localizer T { get; set; }

        public AutoroutePartHandler(
            IRepository<AutoroutePartRecord> autoroutePartRepository,
            Lazy<IAutorouteService> autorouteService,
            IOrchardServices orchardServices,
            IAliasService aliasService) {

            Filters.Add(StorageFilter.For(autoroutePartRepository));
            _autorouteService = autorouteService;
            _orchardServices = orchardServices;
            _aliasService = aliasService;

            OnUpdated<AutoroutePart>((ctx, part) => CreateAlias(part));

            OnCreated<AutoroutePart>((ctx, part) => {
                // non-draftable items
                if (part.ContentItem.VersionRecord == null) {
                    PublishAlias(part);
                }
            });

            // OnVersioned<AutoroutePart>((ctx, part1, part2) => CreateAlias(part1));

            OnPublished<AutoroutePart>((ctx, part) => PublishAlias(part));

            // Remove alias if removed or unpublished
            OnRemoved<AutoroutePart>((ctx, part) => RemoveAlias(part));
            OnUnpublished<AutoroutePart>((ctx, part) => RemoveAlias(part));

            // Register alias as identity
            OnGetContentItemMetadata<AutoroutePart>((ctx, part) => {
                if (part.DisplayAlias != null)
                    ctx.Metadata.Identity.Add("alias", part.DisplayAlias);
            });
        }

        private void CreateAlias(AutoroutePart part) {
            ProcessAlias(part);
        }

        private void PublishAlias(AutoroutePart part) {
            ProcessAlias(part);

            // should it become the home page ?
            if (part.DisplayAlias == "/") {
                part.DisplayAlias = String.Empty;

                // regenerate the alias for the previous home page
                var currentHomePages = _orchardServices.ContentManager.Query<AutoroutePart, AutoroutePartRecord>().Where(x => x.DisplayAlias == "").List();
                foreach (var current in currentHomePages.Where(x => x.Id != part.Id)) {
                    if (current != null) {
                        current.CustomPattern = String.Empty; // force the regeneration
                        current.DisplayAlias = _autorouteService.Value.GenerateAlias(current);
                    }
                    _autorouteService.Value.PublishAlias(current);
                }
            }

            _autorouteService.Value.PublishAlias(part);
        }

        private void ProcessAlias(AutoroutePart part) {
            // generate an alias if one as not already been entered
            if (String.IsNullOrWhiteSpace(part.DisplayAlias)) {
                part.DisplayAlias = _autorouteService.Value.GenerateAlias(part);
            }

            // if the generated alias is empty, compute a new one 
            if (String.IsNullOrWhiteSpace(part.DisplayAlias)) {
                _autorouteService.Value.ProcessPath(part);
                _orchardServices.Notifier.Warning(T("The permalink could not be generated, a new slug has been defined: \"{0}\"", part.Path));
                return;
            }

            // check for permalink conflict, unless we are trying to set the home page
            if (part.DisplayAlias != "/") {
                var previous = part.Path;
                if (!_autorouteService.Value.ProcessPath(part))
                    _orchardServices.Notifier.Warning(T("Permalinks in conflict. \"{0}\" is already set for a previously created {2} so now it has the slug \"{1}\"",
                                                 previous, part.Path, part.ContentItem.ContentType));
            }
        }

        void RemoveAlias(AutoroutePart part) {
            // Is this the current home page?
            var homePageRoute = _aliasService.Get("");
            var homePageId = homePageRoute.ContainsKey("id") ? int.Parse(homePageRoute["id"].ToString()) : default(int);

            if (part.ContentItem.Id == homePageId) {
                _orchardServices.Notifier.Warning(T("You removed the content item that served as the site\'s home page. \nMost possibly this means that instead of the home page a \"404 Not Found\" error page will be displayed without a link to log in or access the dashboard. \n\nTo prevent this you can e.g. publish a content item that has the \"Set as home page\" checkbox ticked."));
            }
            _autorouteService.Value.RemoveAliases(part);
        }
    }
}

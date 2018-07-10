﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Discounts;
using Nop.Plugin.DiscountRules.HasProducts.Models;
using Nop.Services;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Services.Vendors;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Kendoui;
using Nop.Web.Framework.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Nop.Plugin.DiscountRules.HasProducts.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.Admin)]
    public class DiscountRulesHasProductsController : BasePluginController
    {
        private readonly IDiscountService _discountService;
        private readonly ISettingService _settingService;
        private readonly IPermissionService _permissionService;
        private readonly IWorkContext _workContext;
        private readonly ILocalizationService _localizationService;
        private readonly ICategoryService _categoryService;
        private readonly IManufacturerService _manufacturerService;
        private readonly IStoreService _storeService;
        private readonly IVendorService _vendorService;
        private readonly IProductService _productService;

        public DiscountRulesHasProductsController(IDiscountService discountService,
            ISettingService settingService, 
            IPermissionService permissionService,
            IWorkContext workContext, 
            ILocalizationService localizationService,
             ICategoryService categoryService, 
            IManufacturerService manufacturerService,
            IStoreService storeService, 
            IVendorService vendorService,
            IProductService productService)
        {
            this._discountService = discountService;
            this._settingService = settingService;
            this._permissionService = permissionService;
            this._workContext = workContext;
            this._localizationService = localizationService;
            this._categoryService = categoryService;
            this._manufacturerService = manufacturerService;
            this._storeService = storeService;
            this._vendorService = vendorService;
            this._productService = productService;
        }

        public IActionResult Configure(int discountId, int? discountRequirementId)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageDiscounts))
                return Content("Access denied");

            var discount = _discountService.GetDiscountById(discountId);
            if (discount == null)
                throw new ArgumentException("Discount could not be loaded");

            if (discountRequirementId.HasValue)
            {
                var discountRequirement = discount.DiscountRequirements.FirstOrDefault(dr => dr.Id == discountRequirementId.Value);
                if (discountRequirement == null)
                    return Content("Failed to load requirement.");
            }

            var requirementId = discountRequirementId ?? 0;
            var model = new RequirementModel
            {
                RequirementId = requirementId,
                DiscountId = discountId,
                Products = _settingService.GetSettingByKey<string>($"DiscountRequirement.RestrictedProductIds-{requirementId}"),
                ProductQuantityMin = _settingService.GetSettingByKey<int>($"DiscountRequirement.ProductQuantityMin-{requirementId}"),
                ProductQuantityMax = _settingService.GetSettingByKey<int>($"DiscountRequirement.ProductQuantityMax-{requirementId}")
            };

            //add a prefix
            ViewData.TemplateInfo.HtmlFieldPrefix = $"DiscountRulesHasProducts{requirementId}";

            return View("~/Plugins/DiscountRules.HasProducts/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AdminAntiForgery]
        public IActionResult Configure(int discountId, int? discountRequirementId, int productQuantityMin, int productQuantityMax, string productIds)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageDiscounts))
                return Json(new { Result = false, ErrorMessage = "Access denied" });

            if (string.IsNullOrEmpty(productIds))
                return Json(new { Result = false, ErrorMessage = "Please select at least one product" });

            if (productQuantityMin <= 0 || productQuantityMax <= 0)
                return Json(new { Result = false, ErrorMessage = "Minimum and maximum quantities should be greater than zero" });

            if (productQuantityMin > productQuantityMax)
                return Json(new { Result = false, ErrorMessage = "Max quantity should be greater than minimum quantity" });

            var discount = _discountService.GetDiscountById(discountId);
            if (discount == null)
                return Json(new { Result = false, ErrorMessage = "Discount could not be loaded" });

            DiscountRequirement discountRequirement = null;
            if (discountRequirementId.HasValue)
                discountRequirement = discount.DiscountRequirements.FirstOrDefault(dr => dr.Id == discountRequirementId.Value);

            if (discountRequirement == null)
            {
                discountRequirement = new DiscountRequirement
                {
                    DiscountRequirementRuleSystemName = "DiscountRequirement.HasProducts"
                };
                discount.DiscountRequirements.Add(discountRequirement);
                _discountService.UpdateDiscount(discount);
            }

            if (productQuantityMin > 0 && productQuantityMax > 0 && productQuantityMin <= productQuantityMax)
            {
                _settingService.SetSetting($"DiscountRequirement.ProductQuantityMin-{discountRequirement.Id}", productQuantityMin);
                _settingService.SetSetting($"DiscountRequirement.ProductQuantityMax-{discountRequirement.Id}", productQuantityMax);
            }
            _settingService.SetSetting($"DiscountRequirement.RestrictedProductIds-{discountRequirement.Id}", productIds);

            return Json(new { Result = true, NewRequirementId = discountRequirement.Id });
        }

        public IActionResult ProductAddPopup()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageProducts))
                return Content("Access denied");

            var model = new RequirementModel.AddProductModel
            {
                //a vendor should have access only to his products
                IsLoggedInAsVendor = _workContext.CurrentVendor != null
            };

            //categories
            model.AvailableCategories.Add(new SelectListItem { Text = _localizationService.GetResource("Admin.Common.All"), Value = "0" });
            var categories = _categoryService.GetAllCategories(showHidden: true);
            foreach (var c in categories)
                model.AvailableCategories.Add(new SelectListItem { Text = c.GetFormattedBreadCrumb(categories), Value = c.Id.ToString() });

            //manufacturers
            model.AvailableManufacturers.Add(new SelectListItem { Text = _localizationService.GetResource("Admin.Common.All"), Value = "0" });
            foreach (var m in _manufacturerService.GetAllManufacturers(showHidden: true))
                model.AvailableManufacturers.Add(new SelectListItem { Text = m.Name, Value = m.Id.ToString() });

            //stores
            model.AvailableStores.Add(new SelectListItem { Text = _localizationService.GetResource("Admin.Common.All"), Value = "0" });
            foreach (var s in _storeService.GetAllStores())
                model.AvailableStores.Add(new SelectListItem { Text = s.Name, Value = s.Id.ToString() });

            //vendors
            model.AvailableVendors.Add(new SelectListItem { Text = _localizationService.GetResource("Admin.Common.All"), Value = "0" });
            foreach (var v in _vendorService.GetAllVendors(showHidden: true))
                model.AvailableVendors.Add(new SelectListItem { Text = v.Name, Value = v.Id.ToString() });

            //product types
            model.AvailableProductTypes = ProductType.SimpleProduct.ToSelectList(false).ToList();
            model.AvailableProductTypes.Insert(0, new SelectListItem { Text = _localizationService.GetResource("Admin.Common.All"), Value = "0" });

            return View("~/Plugins/DiscountRules.HasProducts/Views/ProductAddPopup.cshtml", model);
        }

        [HttpPost]
        [AdminAntiForgery]
        public IActionResult ProductAddPopupList(DataSourceRequest command, RequirementModel.AddProductModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageProducts))
                return Content("Access denied");

            //a vendor should have access only to his products
            if (_workContext.CurrentVendor != null)
                model.SearchVendorId = _workContext.CurrentVendor.Id;

            var products = _productService.SearchProducts(
                categoryIds: new List<int> { model.SearchCategoryId },
                manufacturerId: model.SearchManufacturerId,
                storeId: model.SearchStoreId,
                vendorId: model.SearchVendorId,
                productType: model.SearchProductTypeId > 0 ? (ProductType?)model.SearchProductTypeId : null,
                keywords: model.SearchProductName,
                pageIndex: command.Page - 1,
                pageSize: command.PageSize,
                showHidden: true
                );

            return Json(GetGridModel(products, products.TotalCount));
        }

        [HttpPost]
        [AdminAntiForgery]
        public IActionResult ProductSelectedPopupList(string selectedProductIds)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageProducts))
                return Content("Access denied");

            var products = _productService.GetProductsByIds(Array.ConvertAll(selectedProductIds.Split(','), int.Parse));
            return Json(GetGridModel(products, products.Count()));
        }

        [HttpPost]
        [AdminAntiForgery]
        public IActionResult LoadProductFriendlyNames(string productIds)
        {
            var result = "";
            var hasManageProductsPermission = _permissionService.Authorize(StandardPermissionProvider.ManageProducts);

            if (hasManageProductsPermission && !String.IsNullOrWhiteSpace(productIds))
            {
                //we support comma-separated list of product identifiers (e.g. 77, 123, 156).
                var rangeArray = productIds
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .ToList();

                //try to parse product ids
                var ids = new List<int>();
                foreach (string str1 in rangeArray)
                    if (int.TryParse(str1, out int productId))
                        ids.Add(productId);

                //prepare product names
                var products = _productService.GetProductsByIds(ids.ToArray());
                var productNames = new List<string>();

                for (int i = 0; i <= products.Count - 1; i++)
                    productNames.Add(products[i].Name);

                result = string.Join(", ", productNames);
            }

            return Json(new { Text = result });
        }

        private DataSourceResult GetGridModel(IList<Product> products, int total)
        {
            return new DataSourceResult
            {
                Data = products.Select(x => new RequirementModel.ProductModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Published = x.Published
                }),
                Total = total
            };
        }
    }
}
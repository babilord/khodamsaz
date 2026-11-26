using HandmadeMarketplace.Application;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HandmadeMarketplace.Web.Models;

// Only price properties use this binder; IDs and other numeric values keep MVC defaults.
public sealed class PriceModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.PropertyName is "Price" or "Min" or "Max" &&
        (context.Metadata.ModelType == typeof(long) || context.Metadata.ModelType == typeof(long?))
            ? new PriceModelBinder() : null;
}

public sealed class PriceModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var input = context.ValueProvider.GetValue(context.ModelName);
        if (input == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, input);
        if (string.IsNullOrWhiteSpace(input.FirstValue) && context.ModelType == typeof(long?))
            context.Result = ModelBindingResult.Success(null);
        else if (PersianPrice.TryParse(input.FirstValue, out var value) && value <= 1_000_000_000_000)
            context.Result = ModelBindingResult.Success(value);
        else context.ModelState.TryAddModelError(context.ModelName, "قیمت باید عددی بین صفر و ۱٬۰۰۰٬۰۰۰٬۰۰۰٬۰۰۰ تومان باشد؛ برای قیمت توافقی صفر وارد کنید.");
        return Task.CompletedTask;
    }
}

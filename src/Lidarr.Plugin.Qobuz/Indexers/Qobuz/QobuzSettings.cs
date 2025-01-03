using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Qobuz
{
    public class QobuzIndexerSettingsValidator : AbstractValidator<QobuzIndexerSettings>
    {
        public QobuzIndexerSettingsValidator()
        {
        }
    }

    public class QobuzIndexerSettings : IIndexerSettings
    {
        private static readonly QobuzIndexerSettingsValidator Validator = new QobuzIndexerSettingsValidator();

        [FieldDefinition(0, Label = "Qobuz Email", Type = FieldType.Textbox, HelpTextWarning = "If an email+password and an id+token are supplied at the same time, the email/password will be used. Only one form of authentication is needed.")]
        public string Email { get; set; } = "";

        [FieldDefinition(1, Label = "Qobuz Password (MD5)", Type = FieldType.Textbox)]
        public string MD5Password { get; set; } = "";

        [FieldDefinition(2, Label = "User ID", Type = FieldType.Textbox)]
        public string UserID { get; set; }

        [FieldDefinition(3, Label = "User Auth Token", Type = FieldType.Textbox, HelpTextWarning = "If a token sign in is failing, try changing the App ID and Secret.", HelpLink = "https://telegra.ph/How-to-fix-403---Invalid-usernameemail-and-password-10-12")]
        public string UserAuthToken { get; set; }

        [FieldDefinition(4, Label = "App ID", Type = FieldType.Textbox, Placeholder = "Optional")]
        public string AppID { get; set; }

        [FieldDefinition(5, Label = "App Secret", Type = FieldType.Textbox, Placeholder = "Optional")]
        public string AppSecret { get; set; }

        [FieldDefinition(6, Type = FieldType.Number, Label = "Early Download Limit", Unit = "days", HelpText = "Time before release date Lidarr will download from this indexer, empty is no limit", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        // this is hardcoded so this doesn't need to exist except that it's required by the interface
        public string BaseUrl { get; set; } = "";

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}

import React from "react";

interface AppSnackbarProps {
  message: string;
  isVisible: boolean;
}

export const AppSnackbar: React.FC<AppSnackbarProps> = ({ message, isVisible }) => {
  if (!isVisible || !message) {
    return null;
  }

  return (
    <div className="md3-snackbar" role="status" aria-live="polite">
      {message}
    </div>
  );
};

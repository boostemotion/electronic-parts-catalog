import React from "react";

type AppIconName = "catalog" | "sync" | "about" | "search" | "add";

interface AppIconProps {
  name: AppIconName;
  className?: string;
}

export const AppIcon: React.FC<AppIconProps> = ({ name, className }) => {
  const paths: Record<AppIconName, React.ReactNode> = {
    catalog: (
      <>
        <rect x="4" y="6" width="16" height="4" rx="2" />
        <rect x="4" y="12" width="16" height="4" rx="2" />
        <rect x="4" y="18" width="11" height="4" rx="2" />
      </>
    ),
    sync: (
      <>
        <path d="M6 8a7 7 0 0 1 11.62-2.9" />
        <path d="M18 4.5v4h-4" />
        <path d="M18 16a7 7 0 0 1-11.62 2.9" />
        <path d="M6 19.5v-4h4" />
      </>
    ),
    about: (
      <>
        <circle cx="12" cy="12" r="8" />
        <path d="M12 10v5" />
        <circle cx="12" cy="7.5" r="0.9" fill="currentColor" stroke="none" />
      </>
    ),
    search: (
      <>
        <circle cx="11" cy="11" r="5.5" />
        <path d="M15.5 15.5 20 20" />
      </>
    ),
    add: (
      <>
        <path d="M12 5v14" />
        <path d="M5 12h14" />
      </>
    ),
  };

  return (
    <svg
      aria-hidden="true"
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {paths[name]}
    </svg>
  );
};

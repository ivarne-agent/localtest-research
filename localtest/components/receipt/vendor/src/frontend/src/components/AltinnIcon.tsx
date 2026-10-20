import { createTheme } from '@material-ui/core';
import { withStyles } from '@material-ui/core/styles';
import classNames from 'classnames';
import React from 'react';
import altinnTheme from 'src/theme/altinnStudioTheme';

export interface IAltinnIconCompontentProvidedProps {
  iconClass: string;
  isActive?: boolean;
  isActiveIconColor?: string;
  iconColor: any;
  iconSize?: number|string;
  padding?: string;
  margin?: string;
  weight?: number;
}

const theme = createTheme(altinnTheme);

const styles = {
  activeIcon: {
    color: theme.altinnPalette.primary.blueDark,
  },
};

export class AltinnIcon extends React.Component<IAltinnIconCompontentProvidedProps> {
  public render() {
    return (
      <i
        className={
          classNames(
            this.props.iconClass,
          )}
        style={{
          color: this.props.isActive ? this.props.isActiveIconColor : this.props.iconColor,
          fontSize: this.props.iconSize ? this.props.iconSize : undefined,
          fontWeight: this.props.weight ? this.props.weight : undefined,
          margin: this.props.margin ? this.props.margin : undefined,
          padding: this.props.padding ? this.props.padding : undefined,
        }}
      />
    );
  }
}

export default withStyles(styles)(AltinnIcon);
